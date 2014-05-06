﻿using System;
using System.Collections.Generic;
using System.IO;
using MTDBFramework.Algorithms.RetentionTimePrediction;
using MTDBFramework.Data;
using MTDBFramework.Database;
using PHRPReader;

namespace MTDBFramework.IO
{
    /// <summary>
    /// Summary Description for SequestPHRP Reader
    /// </summary>
    public class SequestPhrpReader : IPhrpReader
    {
        public Options ReaderOptions { get; set; }

        public SequestPhrpReader(Options options)
        {
            ReaderOptions = options;
        }

        public LcmsDataSet Read(string path)
        {
            var results = new List<SequestResult>();
            var filter = new SequestTargetFilter(ReaderOptions);

            var proteinInfos = new Dictionary<int, ProteinInformation>();

            // Get the Evidences using PHRPReader which looks at the path that was passed in
            var reader = new clsPHRPReader(path);
            while (reader.CanRead)
            {
                var result = new SequestResult();
                reader.MoveNext();

                result.AnalysisId = reader.CurrentPSM.ResultID;
                result.Charge = reader.CurrentPSM.Charge;
                result.CleanPeptide = reader.CurrentPSM.PeptideCleanSequence;
                result.SeqWithNumericMods = reader.CurrentPSM.PeptideWithNumericMods;
                result.MonoisotopicMass = reader.CurrentPSM.PeptideMonoisotopicMass;
                result.ObservedMonoisotopicMass = reader.CurrentPSM.PrecursorNeutralMass;
                result.MultiProteinCount = (short)reader.CurrentPSM.Proteins.Count;
                result.Scan = reader.CurrentPSM.ScanNumber;
                result.Sequence = reader.CurrentPSM.Peptide;
                result.Mz = clsPeptideMassCalculator.ConvoluteMass(reader.CurrentPSM.PrecursorNeutralMass, 0, reader.CurrentPSM.Charge);
                if (reader.CurrentPSM.MSGFSpecProb.Length != 0)
                {
                    result.SpecProb = Convert.ToDouble(reader.CurrentPSM.MSGFSpecProb);
                }

                result.PeptideInfo = new TargetPeptideInfo
                {
                    Peptide = result.Sequence,
                    CleanPeptide = result.CleanPeptide,
                    PeptideWithNumericMods = result.SeqWithNumericMods
                };

                result.DelCn = Convert.ToDouble(reader.CurrentPSM.AdditionalScores["DelCn"]);
                result.DelCn2 = Convert.ToDouble(reader.CurrentPSM.AdditionalScores["DelCn2"]);
                result.DelM = Convert.ToDouble(reader.CurrentPSM.MassErrorDa);
                if (reader.CurrentPSM.MassErrorPPM.Length != 0)
                {
                    result.DelMPpm = Convert.ToDouble(reader.CurrentPSM.MassErrorPPM);
                }
                result.NumTrypticEnds = Convert.ToInt16(reader.CurrentPSM.AdditionalScores["NumTrypticEnds"]);
                result.RankSp = Convert.ToInt16(reader.CurrentPSM.AdditionalScores["RankSp"]);
                result.RankXc = Convert.ToInt16(reader.CurrentPSM.AdditionalScores["RankXc"]);
                result.Sp = Convert.ToDouble(reader.CurrentPSM.AdditionalScores["Sp"]);
                result.XCorr = Convert.ToDouble(reader.CurrentPSM.AdditionalScores["XCorr"]);
                result.XcRatio = Convert.ToDouble(reader.CurrentPSM.AdditionalScores["XcRatio"]);

                result.FScore = SequestResult.CalculatePeptideProphetDistriminantScore(result);

				// If it passes the filter, check for if there are any modifications, add them if needed, and add the result to the list
                if (!filter.ShouldFilter(result))
                {
                    result.DataSet = new TargetDataSet
                    {
                        Path = path,
                        Name = DatasetPathUtility.CleanPath(path)
                    };

                    result.ModificationCount = (short)reader.CurrentPSM.ModifiedResidues.Count;
                    result.SeqInfoMonoisotopicMass = result.MonoisotopicMass;

                    foreach (var p in reader.CurrentPSM.ProteinDetails)
                    {
                        var protein = new ProteinInformation
                        {
                            ProteinName = p.ProteinName,
                            CleavageState = p.CleavageState,
                            TerminusState = p.TerminusState,
                            ResidueStart = p.ResidueStart,
                            ResidueEnd = p.ResidueEnd
                        };

                        if (proteinInfos.ContainsValue(protein))
                        {
                            result.Proteins.Add(protein);
                        }
                        else
                        {
                            proteinInfos.Add((proteinInfos.Count + 1), protein);
                            result.Proteins.Add(protein);
                        }
                    }

                    if (result.ModificationCount != 0)
                    {
                        foreach (var info in reader.CurrentPSM.ModifiedResidues)
                        {
                            result.SeqInfoMonoisotopicMass += info.ModDefinition.ModificationMass;
                            result.ModificationDescription += info.ModDefinition.MassCorrectionTag + ":" + info.ResidueLocInPeptide + " ";
                        }
                    }

                    results.Add(result);
                }
            }
            AnalysisReaderHelper.CalculateObservedNet(results);
            AnalysisReaderHelper.CalculatePredictedNet(RetentionTimePredictorFactory.CreatePredictor(ReaderOptions.PredictorType), results);

            return new LcmsDataSet(Path.GetFileNameWithoutExtension(path), LcmsIdentificationTool.Sequest, results);
        }
    }
}
