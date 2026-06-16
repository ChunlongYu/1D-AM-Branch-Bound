using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ClosedXML.Excel.XLPredefinedFormat;

namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    public class UPMSPInstances
    {
        public int numMachines { get; set; }
        public int numJobs { get; set; }
        public int[] weights { get; set; }
        public double[,] processingTimes { get; set; }
        public double[] upperBoundOnCompletionTimeOfMachine { get; set; }

        public UPMSPInstances(int numMachines, int numJobs, Switcher  switcher, int run)
        {
            this.numMachines = numMachines;
            this.numJobs = numJobs;

            //Read the weights and processing times
            if (switcher.instanceType == "Read")
            {
                this.weights =ReadWeights(switcher);
                this.processingTimes =ReadProcessingTimes(switcher);
            }
            if (switcher.instanceType == "Write")
            {
                if (switcher.processingTimeType == "Integer")
                {
                    this.weights = ProduceWeights(switcher, 1, 100);
                    this.processingTimes = ProduceProcessingTimes(switcher, 1, 30, 0);
                }
                if (switcher.processingTimeType == "Fractional")
                {
                    this.weights = ProduceWeights(switcher, 1, 5);
                    this.processingTimes = ProduceProcessingTimes(switcher, 0, 0, 25.6);
                }
                PrintInstanceToExcel(switcher, run);
            }
            this.upperBoundOnCompletionTimeOfMachine = new double[numMachines];
        }

        /// <summary>
        ///  Read the weights
        /// </summary>
        /// <returns></returns>
        public int[] ReadWeights(Switcher switcher)
        {
            int[] weights = new int[numJobs];
            using (var workbook = new XLWorkbook(switcher.filePath_Instance))
            {
                var worksheet = workbook.Worksheet("Weights");
                if (worksheet != null)
                {
                    for (int i = 0; i < numJobs; i++)
                    {
                        weights[i] = Convert.ToInt32(worksheet.Cell(i + 2, 2).Value);
                    }
                }
                else
                {
                    Console.WriteLine("Worksheet not found.");
                }
            }
            return weights;
        }

        /// <summary>
        /// Read the processing times
        /// </summary>
        /// <returns></returns>
        public double[,] ReadProcessingTimes(Switcher switcher)
        {
            double[,] processingTimes = new double[numJobs, numMachines];
            using (var workbook = new XLWorkbook(switcher.filePath_Instance))
            {
                var worksheet = workbook.Worksheet("Processing times");
                if (worksheet != null)
                {
                    for (int i = 0; i < numMachines; i++)
                    {
                        for (int j = 0; j < numJobs; j++)
                        {
                            processingTimes[j, i] = Convert.ToDouble(worksheet.Cell(i + 2, j + 2).Value);
                            //processingTimes[j, i] = (int)Math.Round(processingTimes[j, i], MidpointRounding.AwayFromZero);
                            //processingTimes[j, i] = (int)Math.Ceiling(processingTimes[j, i]);
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Worksheet not found.");
                }
            }
            return processingTimes;
        }
        /// <summary>
        /// Produce the weights
        /// </summary>
        /// <returns></returns>
        public int[] ProduceWeights(Switcher switcher, int minValue, int maxValue)
        {
            int[] weights = new int[numJobs];
            Random rand = new Random();
            for (int i = 0; i < numJobs; i++)
            {
                weights[i] = rand.Next(minValue, maxValue + 1);
            }
            return weights;
        }
        /// <summary>
        /// Produce processing times
        /// </summary>
        /// <returns></returns>
        public double[,] ProduceProcessingTimes(Switcher switcher, int minValue, int maxValue, double meanValue)
        {
            Random rand = new Random();
            double[,] processingTimes = new double[numJobs, numMachines];
            for (int i = 0; i < numJobs; i++)
            {
                for (int j = 0; j < numMachines; j++)
                {
                    if (switcher.processingTimeType == "Fractional")
                    {
                        processingTimes[i, j] = Math.Round(-meanValue * Math.Log(rand.NextDouble()), 3);
                    }
                    if (switcher.processingTimeType == "Integer")
                    {
                        processingTimes[i, j] = rand.Next(minValue, maxValue + 1);
                    }
                }
            }
            return processingTimes;
        }
        /// <summary>
        /// Calculate the initial upper bound of completion time
        /// </summary>
        /// <returns></returns>
        public double CalculateUpperBoundOnCompletionTime()
        {
            double upperBound = 0;
            // Find the maximum processing time on each machine
            double[] maxProcessingTimeOnEachMachine = new double[numJobs];
            for (int j = 0; j < numJobs; j++)
            {
                double maxProcessingTime = 0;
                for (int k = 0; k < numMachines; k++)
                {
                    if (maxProcessingTime < processingTimes[j, k])
                    {
                        maxProcessingTime = processingTimes[j, k];
                    }
                }
                maxProcessingTimeOnEachMachine[j] = maxProcessingTime;
            }
            upperBound += maxProcessingTimeOnEachMachine.Sum() / numMachines + maxProcessingTimeOnEachMachine.Max();
            //double[] ubs = new double[numMachines];
            //for (int k = 0; k < numMachines; k++)
            //{
            //    for (int j = 0; j < numJobs; j++)
            //    {
            //        ubs[k] += processingTimes[j, k];
            //    }
            //}
            //upperBound = ubs.Max();
            return upperBound;
        }
        /// <summary>
        /// Read the complete times
        /// </summary>
        /// <param name="filePath"></param>
        public  List<double> ReadCompleteTimes(string filePath)
        {
            var completeTimes = new List<double>();
            using (var workbook = new XLWorkbook(filePath))
            {
                var worksheet = workbook.Worksheet("Complete Times");
                if (worksheet != null)
                {
                     completeTimes = new List<double>();

                    int currentRow = 2; 
                    while (!worksheet.Cell(currentRow, 2).IsEmpty())
                    {
                        double completeTime = worksheet.Cell(currentRow, 2).GetValue<double>();
                        completeTimes.Add(completeTime);
                        currentRow++;
                    }
                }
                else
                {
                    Console.WriteLine("Worksheet not found.");
                }
            }
            return completeTimes;
        }
        /// <summary>
        /// Produce instances
        /// </summary>
        /// <param name="weights"></param>
        /// <param name="processingTimes"></param>
        /// <param name="run"></param>
        public void PrintInstanceToExcel(Switcher switcher, int run)
        {
            //string outPutName = "Bulbul Sen Instances\\Inst 0" + switcher.instanceGroup + "\\";
            string outPutName = switcher.dataDirectory;
            Directory.CreateDirectory(outPutName);
            outPutName = Path.Combine(outPutName, numMachines.ToString() + "_" + numJobs.ToString() + "_TWCT_" + (run+1) + ".xlsx");

            using (var workbook = new XLWorkbook())
            {
                var weightsSheet = workbook.Worksheets.Add("Weights");
                weightsSheet.Cell(1, 1).Value = "Job";
                weightsSheet.Cell(1, 2).Value = "Weights";

                for (int k = 0; k < numJobs; k++)
                {
                    weightsSheet.Cell(k + 2, 1).Value = k + 1;
                    weightsSheet.Cell(k + 2, 2).Value = weights[k];
                }

                var processingTimesSheet = workbook.Worksheets.Add("Processing times");
                processingTimesSheet.Cell(1, 1).Value = "Machine\\Job";

                for (int j = 0; j < numJobs; j++)
                {
                    processingTimesSheet.Cell(1, j + 2).Value = j + 1;
                }

                for (int k = 0; k < numMachines; k++)
                {
                    processingTimesSheet.Cell(k + 2, 1).Value = k + 1;
                    for (int j = 0; j < numJobs; j++)
                    {
                        processingTimesSheet.Cell(k + 2, j + 2).Value = processingTimes[j, k];
                    }
                }
                workbook.SaveAs(outPutName);
            }
        }
        /// <summary>
        ///  Obtain precede job ordering restriction
        /// </summary>
        /// <param name="weights"></param>
        /// <param name="processingTimes"></param>
        /// <returns></returns>
        public List<List<List<int>>> ObtainPrecedeJobOrderingRestriction()
        {
            List<List<List<int>>> precedeJobOrderingRestriction = new List<List<List<int>>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<List<int>> precedeJobOrderingRestrictionOneMachine = new List<List<int>>();
                for (int j = 0; j < numJobs; j++)
                {
                    List<int> precedeJobOrderingRestrictionOneJob = new List<int>();
                    double ratio = processingTimes[j, k] / weights[j];
                    // Find the jobs that have a smaller ratio
                    for (int n = 0; n < numJobs; n++)
                    {
                        if (n != j)
                        {
                            double currentRatio = processingTimes[n, k] / weights[n];
                            if (currentRatio < ratio)
                            {
                                precedeJobOrderingRestrictionOneJob.Add(n + 1);
                            }
                        }
                    }
                    precedeJobOrderingRestrictionOneMachine.Add(precedeJobOrderingRestrictionOneJob);
                }
                precedeJobOrderingRestriction.Add(precedeJobOrderingRestrictionOneMachine);
            }
            return precedeJobOrderingRestriction;
        }
        /// <summary>
        ///  Obtain succeed job ordering restriction
        /// </summary>
        /// <param name="weights"></param>
        /// <param name="processingTimes"></param>
        /// <returns></returns>
        public List<List<List<int>>> ObtainSucceedJobOrderingRestriction()
        {
            List<List<List<int>>> succeedJobOrderingRestriction = new List<List<List<int>>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<List<int>> succeedJobOrderingRestrictionOneMachine = new List<List<int>>();
                for (int j = 0; j < numJobs; j++)
                {
                    List<int> succeedJobOrderingRestrictionOneJob = new List<int>();
                    double ratio = processingTimes[j, k] / weights[j];

                    // Find the jobs that have a smaller ratio
                    for (int n = 0; n < numJobs; n++)
                    {
                        if (n != j)
                        {
                            double currentRatio = processingTimes[n, k] / weights[n];
                            if (currentRatio > ratio)
                            {
                                succeedJobOrderingRestrictionOneJob.Add(n + 1);
                            }
                        }
                    }
                    succeedJobOrderingRestrictionOneMachine.Add(succeedJobOrderingRestrictionOneJob);
                }
                succeedJobOrderingRestriction.Add(succeedJobOrderingRestrictionOneMachine);
            }
            return succeedJobOrderingRestriction;
        }
        /// <summary>
        /// Generate the initial initialSolution
        /// </summary>
        /// <returns></returns>
        public Solution GenerateInitialSolution( List<BucketGraph> bucketGraphs)
        {
            Solution initialSolution = new Solution();
            List<List<PartialSchedule>> initialFeasiblePartialScheduleOnAllMachines = new List<List<PartialSchedule>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<PartialSchedule> partialScheduleSetOnSingleMachines = new List<PartialSchedule>();
                PartialSchedule partialScheduleOnSingleMachine = new PartialSchedule();
                partialScheduleOnSingleMachine.setOfProcessedJobs = new List<int> { };
                partialScheduleOnSingleMachine.TWCT = 0;
                partialScheduleOnSingleMachine.vectorOfProcessedJob = new double[numJobs];
                partialScheduleSetOnSingleMachines.Add(partialScheduleOnSingleMachine);
                initialFeasiblePartialScheduleOnAllMachines.Add(partialScheduleSetOnSingleMachines);
            }

            // Generate the initial feasible partial schedule on all machines
            List<List<DynamicShrinkBound>> jobOrderingRestrictionsClone = new List<List<DynamicShrinkBound>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<DynamicShrinkBound> jobOrderingRestriction_Machine = new List<DynamicShrinkBound>();
                for (int j = 0; j < bucketGraphs[k].dynamicShrinkBound.Count; j++)
                {
                    jobOrderingRestriction_Machine.Add(bucketGraphs[k].dynamicShrinkBound[j]);
                }
                jobOrderingRestriction_Machine = jobOrderingRestriction_Machine.OrderBy(x => x.index).ToList();
                jobOrderingRestrictionsClone.Add(jobOrderingRestriction_Machine);
            }

            List<int> jobIndexSet = new List<int>();
            for (int j = 0; j < numJobs; j++)
            {
                jobIndexSet.Add(j + 1);
            }

            while (jobIndexSet.Count != 0)
            {
                for (int k = 0; k < numMachines; k++)
                {

                    double minRatio = double.MaxValue;
                    int minJobIndex = 0;

                    // Find the job with the minimum ratio
                    for (int j = 0; j < jobIndexSet.Count; j++)
                    {
                        double ratio = jobOrderingRestrictionsClone[k][jobIndexSet[j]].ratioProcessingTimeToWeight;
                        if (minRatio > ratio)
                        {
                            minRatio = ratio;
                            minJobIndex = jobIndexSet[j];
                        }
                    }
                    // Add the job to the partial schedule
                    initialFeasiblePartialScheduleOnAllMachines[k][0].setOfProcessedJobs.Add(minJobIndex);
                    double completionTimeOneJob = 0;
                    for (int n = 0; n < initialFeasiblePartialScheduleOnAllMachines[k][0].setOfProcessedJobs.Count; n++)
                    {
                        int index = initialFeasiblePartialScheduleOnAllMachines[k][0].setOfProcessedJobs[n];
                        completionTimeOneJob += processingTimes[index - 1, k];
                        //Console.WriteLine ($"completion time of job {index}: " + completionTimeOneJob);
                    }
                    initialFeasiblePartialScheduleOnAllMachines[k][0].TWCT += weights[minJobIndex - 1] * completionTimeOneJob;
                    initialFeasiblePartialScheduleOnAllMachines[k][0].vectorOfProcessedJob[minJobIndex - 1] = 1;
                    jobIndexSet.Remove(minJobIndex);
                    if (jobIndexSet.Count == 0)
                    {
                        break;
                    }
                }
            }
            initialSolution.usedPartialScheduleSet = initialFeasiblePartialScheduleOnAllMachines;
            initialSolution.objValue = 0;
            initialSolution._isInteger = true;
            initialSolution._isFeasible = true;
            for (int k = 0; k < numMachines; k++)
            {
                initialFeasiblePartialScheduleOnAllMachines[k][0].time = 0;
                foreach (int job in initialFeasiblePartialScheduleOnAllMachines[k][0].setOfProcessedJobs)
                {
                    initialFeasiblePartialScheduleOnAllMachines[k][0].time += processingTimes[job - 1, k];
                }
                initialSolution.objValue += initialFeasiblePartialScheduleOnAllMachines[k][0].TWCT;
            }
            initialSolution.usedLmSRCs = new List<LmSRCOfVertex>();
            initialSolution.yks = new List<double[]>();
            initialSolution.dualsOfJobs = new double[numJobs];
            initialSolution.dualsOfMachines = new double[numMachines];
            initialSolution.dualsOfLmSRCsOfVertex = new List<double>();
            initialSolution.bestSchedules = new List<PartialSchedule>();
            initialSolution.makespan = 0;
            foreach (List<PartialSchedule> partialSchedules in initialSolution.usedPartialScheduleSet)
            {
                if (partialSchedules[0].time > initialSolution.makespan)
                {
                    initialSolution.makespan = partialSchedules[0].time;
                }
                initialSolution.bestSchedules.Add(new PartialSchedule(partialSchedules[0]));
            }
            return initialSolution;
        }
    }
}
