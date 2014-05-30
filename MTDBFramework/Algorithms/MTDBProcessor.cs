﻿#region Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MTDBFramework.Algorithms.Alignment;
using MTDBFramework.Algorithms.Clustering;
using MTDBFramework.Data;
using MTDBFramework.Database;
using PNNLOmics.Algorithms.Alignment.LcmsWarp;
using PNNLOmics.Algorithms.Regression;
using PNNLOmics.Annotations;
using PNNLOmics.Data.Features;
using Remotion.Linq.Collections;

#endregion

namespace MTDBFramework.Algorithms
{
    public class MtdbProcessor : IProcessor
    {
        private static int UmcScanComparison(UMCLight umc1, UMCLight umc2)
        {
            return umc1.Scan.CompareTo(umc2.Scan);
        }

        private static int EvidenceScanComparison(Evidence ev1, Evidence ev2)
        {
            return ev1.Scan.CompareTo(ev2.Scan);
        }

        public event EventHandler<AlignmentCompleteArgs> AlignmentComplete;
        
        public MtdbProcessor(Options options)
        {
            ProcessorOptions = options;
        }

        public Options ProcessorOptions { get; set; }

        [UsedImplicitly]
        public TargetDatabase Process(IEnumerable<LcmsDataSet> dataSets)
        {
            // Deal with DataSetId - Auto increments - Not in this class only
            var evidenceMap     = new Dictionary<int, Evidence>();
            var targetDatabase  = new TargetDatabase();
            var aligner         = TargetAlignmentFactory.Create(ProcessorOptions);
            var clusterer       = TargetClustererFactory.Create(ProcessorOptions.TargetFilterType);
            var epicTargets     = new List<Evidence>();
            
            foreach (var dataSet in dataSets)
            {
                var targetFilter = TargetFilterFactory.Create(dataSet.Tool, ProcessorOptions);
                var alignmentFilter = AlignmentFilterFactory.Create(dataSet.Tool, ProcessorOptions);

                var filteredTargets = new List<Evidence>();
                var alignedTargets = new List<Evidence>();

                foreach (var t in dataSet.Evidences)
                {
                    if (!targetFilter.ShouldFilter(t))
                    {
                        filteredTargets.Add(t);

                        if (!alignmentFilter.ShouldFilter(t))
                        {
                            alignedTargets.Add(t);
                        }
                    }
                }

                epicTargets.AddRange(filteredTargets);

                if (ProcessorOptions.TargetFilterType != TargetWorkflowType.TOP_DOWN)
                {
                    var regressor = LinearRegressorFactory.Create(ProcessorOptions.RegressionType);
                    dataSet.RegressionResult = regressor.CalculateRegression(alignedTargets.Select(t => (double)t.Scan).ToList(), alignedTargets.Select(t => t.PredictedNet).ToList());
                }
                else
                {
                    dataSet.RegressionResult = aligner.AlignTargets(filteredTargets, alignedTargets);
                }
            }

            //Create the database (the list of consensus targets)            
            //Convert the list of targets into a list of MassTagLights for LCMS to use as baseline
            
            // Cluster initially to provide a baseline for LCMSWarp
            var newTargets = clusterer.Cluster(epicTargets);                        
            int i = 0, j = 0;
            var tempConsensusTargets = new List<ConsensusTarget>();
            foreach (var consensusTarget in newTargets)
            {
                consensusTarget.Id = ++i;

                foreach (var target in consensusTarget.Evidences)
                {
                    target.Id = ++j;
                }
                consensusTarget.CalculateStatistics();
                tempConsensusTargets.Add(consensusTarget);
                targetDatabase.AddConsensusTarget(consensusTarget);
            }           

            var massTagLightTargets = new List<UMCLight>();
            foreach (var evidence in tempConsensusTargets)
            {
                var driftStart = double.MaxValue;
                var driftEnd = double.MinValue;

                foreach (var member in evidence.Evidences)
                {
                    driftStart = Math.Min(member.Scan, driftStart);
                    driftEnd = Math.Max(member.Scan, driftEnd);
                }

                foreach (var charge in evidence.Charges)
                {

                    var consensus = new UMCLight
                    {
                        Net = evidence.Net,
                        ChargeState = charge,
                        MassMonoisotopic = evidence.TheoreticalMonoIsotopicMass,
                        Id = evidence.Id,
                        MassMonoisotopicAligned = evidence.TheoreticalMonoIsotopicMass,
                        DriftTime = driftEnd - driftStart,
                        ScanStart = (int)driftStart,
                        ScanEnd = (int)driftEnd,
                    };
                    massTagLightTargets.Add(consensus);
                }
            }

            var alignmentData = new List<LcmsWarpAlignmentData>();
            var options = new LcmsWarpAlignmentOptions();
            var lcmsAligner = new LcmsWarpAdapter(options);

            //Foreach dataset
            foreach (var dataSet in dataSets)
            {
                dataSet.Evidences.Sort(EvidenceScanComparison);
                var umcDataset = new List<UMCLight>();

                foreach (var evidence in dataSet.Evidences)
                {

                    var umcData = new UMCLight
                    {
                        Net = evidence.ObservedNet,
                        ChargeState = evidence.Charge,
                        Mz = evidence.Mz,
                        Scan = evidence.Scan,
                        MassMonoisotopic = evidence.MonoisotopicMass,
                        MassMonoisotopicAligned = evidence.MonoisotopicMass,
                        Id = evidence.Id,
                        ScanStart = evidence.Scan,
                        ScanEnd = evidence.Scan,
                    };
                    umcDataset.Add(umcData);
                }
                
                umcDataset.Sort(UmcScanComparison);

                var alignedData = lcmsAligner.Align(massTagLightTargets, umcDataset);
                alignmentData.Add(alignedData);
                var residualList = new List<UMCLight> {Capacity = alignedData.ResidualData.Mz.Length};
                //Put the residual data into a list of UMCLights
                for (int a = 0; a < alignedData.ResidualData.Mz.Length; a++)
                {
                    var residual = new UMCLight
                    {
                        MassMonoisotopic    = alignedData.ResidualData.MassErrorCorrected[a],
                        Net                 = alignedData.ResidualData.LinearCustomNet[a],
                        Scan                = Convert.ToInt32(alignedData.ResidualData.Scan[a])
                    };
                    //Not actually monoisotopic mass here, it's the corrected massError from the warping
                    residualList.Add(residual);
                }

                residualList.Sort(UmcScanComparison);

                umcDataset.Sort(UmcScanComparison);

                //Copy the residual data back into the umcDataset
                for (int a = 0; a < residualList.Count; a++)
                {
                    umcDataset[a].MassMonoisotopicAligned   = umcDataset[a].MassMonoisotopic -
                                                                residualList[a].MassMonoisotopic;
                    umcDataset[a].Net                       = residualList[a].Net;

                    dataSet.Evidences[a].MonoisotopicMass   = dataSet.Evidences[a].MonoisotopicMass -
                                                                residualList[a].MassMonoisotopic;
                    dataSet.Evidences[a].ObservedNet        = residualList[a].Net;
                }

                foreach(var data in dataSet.Evidences)
                {
                    if(!evidenceMap.ContainsKey(data.Id))
                    {
                        evidenceMap.Add(data.Id, data);
                    }
                }
            }
            if (AlignmentComplete != null)
            {
                AlignmentComplete(this, new AlignmentCompleteArgs(alignmentData));
            }

            foreach(var consensus in tempConsensusTargets)
            {
                for (int evNum = 0; evNum < consensus.Evidences.Count; evNum++)
                {
                    consensus.Evidences[evNum] = evidenceMap[consensus.Evidences[evNum].Id];
                }
            }
            return targetDatabase;
        }
    }
}
