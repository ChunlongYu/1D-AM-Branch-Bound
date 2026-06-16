using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Office2013.Drawing.ChartStyle;
using ILOG.Concert;
using ILOG.CPLEX;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;

namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    public class Auxiliary
    {
        public int numMachines { get; set; }
        public int numJobs { get; set; }
        public Auxiliary() { }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        public Auxiliary(int numMachines, int numJobs)
        {
            this.numMachines = numMachines;
            this.numJobs = numJobs;
        }

        /// <summary>
        /// Display the solution
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="solution"></param>
        public void DisplaySolution(UPMSPInstances instance, Solution solution)
        {
            Console.WriteLine($"------------------------------------------------------------------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"The solution is {solution.objValue}");
            Console.WriteLine($"The schedule is ");
            for (int k = 0; k < instance.numMachines; k++)
            {
                Console.WriteLine("Machine " + (k + 1).ToString() + ": [" + solution.usedPartialScheduleSet[k][0].ToString() + "]");
            }
            Console.WriteLine($"------------------------------------------------------------------------------------------------------------------------------------------------------------------");
        }
        /// <summary>
        /// Display the bucket graphs
        /// </summary>
        /// <param name="bucketGraphs"></param>
        public void DisplayBucketGraphs(List<BucketGraph> bucketGraphs)
        {
            foreach (BucketGraph bucketGraph in bucketGraphs)
            {
                Console.WriteLine($"=================================================================================================");
                foreach (StronglyConnectedComponent stronglyConnectedComponent in bucketGraph.stronglyConnectedComponents)
                {
                    if (bucketGraph.adjListOfBucketArcs[stronglyConnectedComponent.bucket].Count == 0) continue;
                    Console.WriteLine($"The bucket {stronglyConnectedComponent.bucket.vertex} {stronglyConnectedComponent.bucket.index} has {bucketGraph.adjListOfBucketArcs[stronglyConnectedComponent.bucket].Count} arcs");

                    foreach (BucketArc bucketArc in bucketGraph.adjListOfBucketArcs[stronglyConnectedComponent.bucket])
                    {
                        Console.WriteLine($"The bucket arc is to {bucketArc.headBucket.vertex} {bucketArc.headBucket.index}");
                    }
                    Console.WriteLine($"------------------------------------------------------------------------------------------------------------------------------------------------------------------");
                }
            }
        }
        /// <summary>
        /// Display the labels
        /// </summary>
        /// <param name="labels"></param>
        /// <param name="machineID"></param>
        public void DisplayLabels(List<ForwardLabel> labels, int machineID)
        {
            Console.WriteLine($"The {machineID + 1}-th Machine:  ");
            foreach (ForwardLabel nextLabel in labels)
            {
                Console.Write("Reduced Cost: " + nextLabel.reducedCost);
                Console.Write("  Complete Time: " + nextLabel.time);
                Console.Write(" Last job : " + nextLabel.lastJob);
                Console.Write("  Jobs: ");
                foreach (int job in nextLabel.setOfProcessedJobs)
                {
                    Console.Write(job.ToString());
                    Console.Write(" ");
                }
                Console.WriteLine();
            }
            Console.WriteLine(labels.Count);
        }
        /// <summary>
        /// Display the lm-SRCs
        /// </summary>
        /// <param name="lmSRCs"></param>
        /// <param name="solution"></param>
        public void DisplayLmSRCs(List<LmSRCOfVertex> lmSRCs, Solution solution)
        {
            foreach (LmSRCOfVertex lmSRC in lmSRCs)
            {
                Console.WriteLine("The violation of SRC is ：" + lmSRC.violation);
                double sum = 0;
                for (int i = 0; i < numMachines; i++)
                {
                    for (int j = 0; j < solution.yks[i].Length; j++)
                    {
                        sum += solution.yks[i][j] * lmSRCs[0].coeff[i][j];
                    }
                }
                double violation = sum - 1;
                Console.WriteLine("The violation of lm-SRC is ：" + violation);
                Console.WriteLine("-------------------------------------------------------");
            }
        }
        /// <summary>
        /// Check whether two lists are equal
        /// </summary>
        /// <param name="list1"></param>
        /// <param name="list2"></param>
        /// <returns></returns>
        public bool CheckIfListsEqual(List<int> list1, List<int> list2)
        {
            if (list1 == null || list2 == null)
            {
                return false;
            }

            if (list1.Count != list2.Count)
            {
                return false;
            }

            // 逐个比较List中的元素
            for (int i = 0; i < list1.Count; i++)
            {
                if (!EqualityComparer<int>.Default.Equals(list1[i], list2[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public class BranchCutAndPrice
    {
        public BranchCutAndPrice(UPMSPInstances instance, Parameters parameters, Switcher switcher, SolutionInformation solutionInfo)
        {
            List<BucketGraph> forwardBucketGraphs = new List<BucketGraph>();
            List<BucketGraph> backwardBucketGraphs = new List<BucketGraph>();

            // Initialize the job ordering restrictions
            for (int k = 0; k < instance.numMachines; k++)
            {
                BucketGraph forwardBucketGraph = new BucketGraph();
                forwardBucketGraph.InitialOrderingRestriction(instance, k);
                forwardBucketGraphs.Add(forwardBucketGraph);

                BucketGraph backwardBucketGraph = new BucketGraph();
                backwardBucketGraph.InitialOrderingRestriction(instance, k);
                backwardBucketGraphs.Add(backwardBucketGraph);
            }
            DateTime beginTime = DateTime.Now;

            // Generate the initial solution
            Solution initialSolution = instance.GenerateInitialSolution(forwardBucketGraphs);

            // Record the list scheduling solution information
            DateTime previousTime = beginTime;
            DateTime currentTime = DateTime.Now;
            solutionInfo.RecordListSchedulingSolutionInformation(currentTime, previousTime, initialSolution);

            // Obtain job ordering restrictions and dynamic shrink bounds
            for (int k = 0; k < instance.numMachines; k++)
            {
                forwardBucketGraphs[k].ObtainForwardOrderingRestriction();
                forwardBucketGraphs[k].InitialUpperBoundOnVertex(instance, initialSolution, k);
                if (switcher.dynamicShrinkBound) 
                {
                    forwardBucketGraphs[k].CaculateDynamicShrinkBound(instance, parameters, initialSolution, k);
                }
                forwardBucketGraphs[k].dynamicShrinkBound = forwardBucketGraphs[k].dynamicShrinkBound.OrderBy(x => x.index).ToList();

                backwardBucketGraphs[k].ObtainBackwardOrderingRestriction();
                backwardBucketGraphs[k].InitialLowerBoundOnVertex(instance, parameters, initialSolution, k);
                if (switcher.dynamicShrinkBound)
                {
                    backwardBucketGraphs[k].CaculateDynamicShrinkBound(instance, parameters, initialSolution, k);
                    backwardBucketGraphs[k].CaculateBackwardDynamicShrinkBound(instance, parameters, initialSolution, k);
                }
                backwardBucketGraphs[k].dynamicShrinkBound = backwardBucketGraphs[k].dynamicShrinkBound.OrderBy(x => x.index).ToList();
            }

            // Initialize the bucket graphs
            initialSolution.forwardBucketGraphs = new List<BucketGraph>();
            initialSolution.backwardBucketGraphs = new List<BucketGraph>();
            for (int k = 0; k < instance.numMachines; k++)
            {
                BucketGraph forwardBucketGraph = forwardBucketGraphs[k];
                forwardBucketGraph.InitialForwardBucketGraph(k, instance, parameters);
                initialSolution.forwardBucketGraphs.Add(new BucketGraph(forwardBucketGraph));

                BucketGraph backwardBucketGraph = backwardBucketGraphs[k];
                backwardBucketGraph.InitialBackwardBucketGraph(k, instance, parameters);
                initialSolution.backwardBucketGraphs.Add(new BucketGraph(backwardBucketGraph));
            }

            // Record the initial dynamic shrink bounds
            solutionInfo.initialDynamicShrinkBounds = solutionInfo.RecordDynamicShrinkBounds(initialSolution);
            solutionInfo.currentDynamicShrinkBounds = solutionInfo.RecordDynamicShrinkBounds(initialSolution);

            // Update the best solution
            solutionInfo.bestSolution = new Solution(initialSolution);

            //Display the initial bucket graphs
            //auxiliary.DisplayBucketGraphs(initialSolution.forwardBucketGraphs);

            //Display the initial initialSolution
            //auxiliary.DisplaySolution(conf, initialSolution);

            // Initial CPLEX solver
            int[] numSchedules = new int[instance.numMachines];
            for (int k = 0; k < instance.numMachines; k++)
            {
                numSchedules[k] = initialSolution.usedPartialScheduleSet[k].Count;
            }
            RMPSolver RMPSolver = new RMPSolver(instance.numMachines);
            RMPSolver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, RMPSolver, initialSolution.usedPartialScheduleSet);

            // Column Generation  Heuristic
            Solution solution = new Solution(initialSolution);
            ColumnGeneration columnGeneration = new ColumnGeneration();

            if (switcher.neighborhoodSearch)
            {
                columnGeneration = new ColumnGeneration(instance,  parameters, RMPSolver, solution, "NeighborhoodSearch", switcher, solutionInfo);
            }

            columnGeneration = new ColumnGeneration(instance, parameters,RMPSolver, solution, "Exact", switcher, solutionInfo);

            // Record the column generation solution information
            previousTime = currentTime;
            currentTime = DateTime.Now;
            solutionInfo.RecordGenerationSolutionSolutionInformation(currentTime, previousTime, solution);

            // Row-and-Column Generation
            if (!solution._isInteger)
            {
                if (switcher.rowAndColumnGeneration)
                {
                    RowAndColumnGeneration rowAndColumnGeneration = new RowAndColumnGeneration(solution, instance, parameters, RMPSolver, switcher, solutionInfo);

                    // Record the row-and-column generation solution information
                    previousTime = currentTime;
                    currentTime = DateTime.Now;
                    solutionInfo.RecordRowAndColumnGenerationSolutionInformation(currentTime, previousTime, solution);
                }
            }

            if (!solution._isInteger)
            {
                if((solutionInfo.bestSolution.objValue - solution.objValue) / solutionInfo.bestSolution.objValue < 0.1)
                {
                    if (switcher.variableFixing)
                    {
                        VariableFixingByReducedCosts variableFixing = new VariableFixingByReducedCosts(solution, solutionInfo.bestSolution.objValue, instance, parameters, solutionInfo, switcher);

                        if (switcher.dynamicShrinkBound)
                        {
                            // Re-built bucket graphs
                            //for (int k = 0; k < instance.numMachines; k++)
                            //{
                            //    solution.forwardBucketGraphs[k].InitialForwardBucketGraph(k, instance, parameters);
                            //}
                            // Record current dynamic shrink bounds
                            solutionInfo.currentDynamicShrinkBounds = solutionInfo.RecordDynamicShrinkBounds(solution);
                        }

                        // Record the variable fixing solution information
                        previousTime = currentTime;
                        currentTime = DateTime.Now;
                        solutionInfo.RecordVariableFixingSolutionInformation(currentTime, previousTime, solution);
                    }

                    // Route Enumeration
                    if (switcher.routeEnumeration)
                    {
                        RouteEnumeration routeEnumeration = new RouteEnumeration(solution, switcher, instance, parameters, solutionInfo.bestSolution.objValue);

                        //Record the route enumeration solution information
                        previousTime = currentTime;
                        currentTime = DateTime.Now;
                        solutionInfo.RecordScheduleEnumerationSolutionInformation(currentTime, previousTime, solution);
                    }

                    // Non-basic columns are excluded in route enumeration, RMPSolver needs to be updated
                    if (!solution._isInteger)
                    {
                        numSchedules = new int[instance.numMachines];
                        for (int k = 0; k < instance.numMachines; k++)
                        {
                            numSchedules[k] = solution.usedPartialScheduleSet[k].Count;
                        }
                        RMPSolver = new RMPSolver(instance.numMachines);
                        RMPSolver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, RMPSolver, solution.usedPartialScheduleSet);
                    }
                }
            }

            

            // Branch-and-Bound
            if (!solution._isInteger)
            {
                BranchAndBound branchAndBound = new BranchAndBound(instance, parameters, switcher, solutionInfo, solution);

                // Record the branch-and-bound solution information
                previousTime = currentTime;
                currentTime = DateTime.Now;
                solutionInfo.RecordStrongBranchingSolutionInformation(currentTime, previousTime, solution);
            }
            else
            {
                if (solution.objValue < solutionInfo.bestSolution.objValue)
                {
                    solutionInfo.bestSolution = new Solution(solution);
                }
            }

            DateTime endTime = DateTime.Now;
            Console.WriteLine("============================================");
            Console.WriteLine($"The running time is: {(endTime - beginTime).TotalSeconds}");
            Console.WriteLine($"The objective value is : {solutionInfo.bestSolution.objValue}");
            solutionInfo.totalRunningTime = (endTime - beginTime).TotalSeconds;
            foreach (PartialSchedule partialSchedule in solutionInfo.bestSolution.bestSchedules)
            {
                Console.WriteLine(partialSchedule.ToString());
            }

        }
    }

    public class ColumnGeneration
    {
        public ColumnGeneration() { }
        /// <summary>
        ///  Column generation operation
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="solver"></param>
        /// <param name="newSolution"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <returns></returns>
        public ColumnGeneration(UPMSPInstances instance, Parameters parameters, RMPSolver solver, Solution solution, string directions, Switcher switcher, SolutionInformation solutionInfo)
        {
            DualPriceSmoothing dualPriceSmoothing = new DualPriceSmoothing();
            Random random = new Random();

            // Parameters for neighborhood search
            int numSameObjValue = 0;
            double lastObjValue = 0;

            // Parameters for dual values smoothing
            // _isSmoothing = false means the dual price smoothing is not used
            bool _isSmoothing = false;
            if (directions == "Exact")
            {
                _isSmoothing = switcher.dualPriceSmoothing;
            }
            int iteration = 0;
            double alpha = 0.5;
            int numberOfMisPricing = 0;

            double[] currentBestReducedCost = new double[instance.numMachines];
            double[] bestReducedCost = new double[instance.numMachines];

            double[] bestDualsOfJobs = new double[instance.numJobs];
            double[] bestDualsOfMachines = new double[instance.numMachines];
            List<double> bestDualsOfLmSRCs = new List<double>();

            double[] currentDualsOfJobs = new double[instance.numJobs];
            double[] currentDualsOfMachines = new double[instance.numMachines];
            List<double> currentDualsOfLmSRCs = new List<double>();

            //---------- flag that a subproblem has been terminated or not: 0, continues; 1,  terminates. ------------
            double[] flag = new double[instance.numMachines];

            while (true)
            {
                solver.model.Solve();

                if (solver.model.GetStatus() == Cplex.Status.Optimal)
                {
                    solution._isFeasible = true;
                    Console.WriteLine("The objective objValue of RMP is： " + solver.model.ObjValue);
                    solution.objValue = solver.model.ObjValue;

                    solution.yks = new List<double[]>();
                    for (int k = 0; k < instance.numMachines; k++)
                    {
                        double[] ys = solver.model.GetValues(solver.varYks[k].ToArray());
                        solution.yks.Add(ys);
                    }
                    solution.dualsOfJobs = solver.model.GetDuals(solver.constaintOfJobs.ToArray());
                    solution.dualsOfMachines = solver.model.GetDuals(solver.constaintOfMachines.ToArray());
                    if (solution.usedLmSRCs.Count == 0)
                    {
                        solution.dualsOfLmSRCsOfVertex = new List<double>();
                    }
                    else
                    {
                        solution.dualsOfLmSRCsOfVertex = solver.model.GetDuals(solver.constaintOfLmSRC.ToArray()).ToList();
                    }

                    // Dual values smooth is used
                    if (_isSmoothing)
                    {
                        // Update the dual values
                        currentDualsOfJobs = solution.dualsOfJobs.ToArray();
                        currentDualsOfMachines = solution.dualsOfMachines.ToArray();
                        currentDualsOfLmSRCs = solution.dualsOfLmSRCsOfVertex.ToList();

                        if (iteration == 0)
                        {
                            bestDualsOfJobs = currentDualsOfJobs.ToArray();
                            bestDualsOfMachines = currentDualsOfMachines.ToArray();
                            bestDualsOfLmSRCs = currentDualsOfLmSRCs.ToList();
                        }

                        // Obtain smooth dual values
                        for (int j = 0; j < instance.numJobs; j++)
                        {
                            solution.dualsOfJobs[j] = alpha * bestDualsOfJobs[j] + (1 - alpha) * currentDualsOfJobs[j];
                        }
                        for (int k = 0; k < instance.numMachines; k++)
                        {
                            solution.dualsOfMachines[k] = alpha * bestDualsOfMachines[k] + (1 - alpha) * currentDualsOfMachines[k];
                        }
                        for (int o = 0; o < solution.usedLmSRCs.Count; o++)
                        {
                            solution.dualsOfLmSRCsOfVertex[o] = alpha * bestDualsOfLmSRCs[o] + (1 - alpha) * currentDualsOfLmSRCs[o];
                        }
                    }

                    PSPSolver labelAlgorithm = new PSPSolver();

                    for (int k = 0; k < instance.numMachines; k++)
                    {
                        flag[k] = 0;
                        double threshold = solution.forwardBucketGraphs[k].dynamicShrinkBound[0].upperBound;
                        double backwardThreshold = 0;

                        // Find schedules with negative reduced cost
                        List<ForwardLabel> newLabelsOnSingleMachine = new List<ForwardLabel>();
                        List<BackwardLabel> newBackwardLabelsOnSingleMachine = new List<BackwardLabel>();
                        switch (directions)
                        {
                            case "NeighborhoodSearch":
                                HeuristicPricing heuristicPricing = new HeuristicPricing();
                                newLabelsOnSingleMachine = heuristicPricing.NeighborhoodSearch(solution, instance, parameters, k, threshold, random);
                                if (solution.objValue == lastObjValue)
                                {
                                    numSameObjValue++;
                                    if (numSameObjValue > parameters.maxNumSameObjValue)
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    numSameObjValue = 0;
                                }
                                lastObjValue = solution.objValue;
                                for (int l = 0; l < newLabelsOnSingleMachine.Count; l++)
                                {
                                    if (CheckIfLabelExists(newLabelsOnSingleMachine[l], solution.usedPartialScheduleSet[k]))
                                    {
                                        newLabelsOnSingleMachine.RemoveAt(l);
                                        l--;
                                    }
                                }
                                break;

                            case "Exact":
                                switch (switcher.exactDirection)
                                {
                                    case "Forward":
                                        solution.forwardBucketGraphs[k] = labelAlgorithm.ForwardLabelAlgorithm(solution, "Forward", k, threshold, instance, parameters, switcher);
                                        //solution.forwardBucketGraphs[k] = labelAlgorithm.GeneralForwardLabelAlgorithm(solution, "Forward", k, threshold, instance, parameters, switcher);
                                        newLabelsOnSingleMachine = new List<ForwardLabel>(solution.forwardBucketGraphs[k].nonDominatedLabelsSet.ToList());
                                        newLabelsOnSingleMachine = newLabelsOnSingleMachine.Where(x => x.reducedCost < -0.01).ToList();
                                        newLabelsOnSingleMachine = newLabelsOnSingleMachine.OrderBy(x => x.reducedCost).ToList();
                                        newLabelsOnSingleMachine = SelectNewLabels(parameters, newLabelsOnSingleMachine, solution.usedPartialScheduleSet[k]);
                                        break;
                                    case "Backward":
                                        backwardThreshold = 0;
                                        solution.backwardBucketGraphs[k] = labelAlgorithm.BackwardLabelAlgorithm(solution, "Backward", k, backwardThreshold, instance, parameters, switcher);
                                        newBackwardLabelsOnSingleMachine = new List<BackwardLabel>(solution.backwardBucketGraphs[k].nonDominatedBackwardLabelsSet.ToList());
                                        newBackwardLabelsOnSingleMachine = newBackwardLabelsOnSingleMachine.Where(x => x.baseReducedCost < -0.01).ToList();
                                        newBackwardLabelsOnSingleMachine = newBackwardLabelsOnSingleMachine.OrderBy(x => x.baseReducedCost).ToList();
                                        foreach (BackwardLabel label in newBackwardLabelsOnSingleMachine)
                                        {
                                            label.setOfProcessedJobs.Reverse();
                                            label.setOfBucketIndex.Reverse();
                                        }
                                        newBackwardLabelsOnSingleMachine = SelectNewBackwardLabels(parameters, newBackwardLabelsOnSingleMachine, solution.usedPartialScheduleSet[k]);
                                        break;
                                    case "BiDirectional":
                                        double forwardThreshold = Math.Floor(threshold * 0.5);
                                        backwardThreshold = threshold - forwardThreshold;
                                        newLabelsOnSingleMachine = labelAlgorithm.BiDirectionalLabelAlgorithm(solution, k, forwardThreshold, backwardThreshold, instance, parameters, switcher);
                                        newLabelsOnSingleMachine = SelectNewLabels(parameters, newLabelsOnSingleMachine, solution.usedPartialScheduleSet[k]);
                                        break;
                                }
                                break;
                        }

                        //Auxiliary auxiliary = new Auxiliary(conf.numMachines, conf.numJobs);
                        //auxiliary.DisplayLabels(newLabelsOnSingleMachine, k);
                        if (directions == "NeighborhoodSearch" || switcher.exactDirection == "Forward" || switcher.exactDirection == "BiDirectional")
                        {
                            if (newLabelsOnSingleMachine.Count == 0)
                            {
                                flag[k] = 1;
                            }
                            else if (_isSmoothing)
                            {
                                // Store the best reduced cost
                                currentBestReducedCost[k] = newLabelsOnSingleMachine[0].reducedCost;
                                if (iteration == 0)
                                {
                                    bestReducedCost[k] = currentBestReducedCost[k];
                                }
                            }

                        }
                        else 
                        {
                            if (newBackwardLabelsOnSingleMachine.Count == 0)
                            {
                                flag[k] = 1;
                            }
                            else if (_isSmoothing)
                            {
                                // Store the best reduced cost
                                currentBestReducedCost[k] = newLabelsOnSingleMachine[0].reducedCost;
                                if (iteration == 0)
                                {
                                    bestReducedCost[k] = currentBestReducedCost[k];
                                }
                            }
                        }

                        // Obtain new columns and then add them into the model
                        List<PartialSchedule> newPartialScheduleOnSingleMachine = ConvertLabelsToPartialSchedule(k, instance.numJobs, instance.processingTimes, instance.weights, newLabelsOnSingleMachine);
                        newPartialScheduleOnSingleMachine.AddRange(ConvertBackwardLabelsToPartialSchedule(k, instance.numJobs, instance.processingTimes, instance.weights, newBackwardLabelsOnSingleMachine));
                        foreach (var schedule in newPartialScheduleOnSingleMachine)
                        {
                            solver.AddColumn(k, instance.numJobs, solver, schedule, solution.usedLmSRCs);
                            solution.usedPartialScheduleSet[k].Add(schedule);
                        }
                    }

                    // New lm-SRCs dual values are obtained
                    if (_isSmoothing)
                    {
                        // Update the best dual values
                        double boundOnBestDuals = dualPriceSmoothing.CalculateLagrangianRelaxationBound(bestDualsOfJobs, bestDualsOfMachines, bestDualsOfLmSRCs, bestReducedCost);
                        double boundOnSmoothDuals = dualPriceSmoothing.CalculateLagrangianRelaxationBound(solution.dualsOfJobs, solution.dualsOfMachines, solution.dualsOfLmSRCsOfVertex, currentBestReducedCost);

                        if (boundOnSmoothDuals > boundOnBestDuals)
                        {
                            bestDualsOfJobs = solution.dualsOfJobs.ToArray();
                            bestDualsOfMachines = solution.dualsOfMachines.ToArray();
                            bestDualsOfLmSRCs = solution.dualsOfLmSRCsOfVertex.ToList();
                            bestReducedCost = currentBestReducedCost.ToArray();
                        }

                        // If a mis-pricing occurs
                        if (flag.Sum() == instance.numMachines)
                        {
                            numberOfMisPricing = numberOfMisPricing + 1;
                            alpha = 1 - numberOfMisPricing * (1 - alpha);
                            if (alpha < 0)
                            {
                                alpha = 0;
                            }
                            // Dual objValue smooth termination
                            if (alpha == 0) _isSmoothing = false;
                            continue;
                        }
                        else
                        {
                            // Calculate angle
                            double angle = dualPriceSmoothing.CalculateAngle(solution, instance.numJobs, instance.numMachines, currentDualsOfJobs, bestDualsOfJobs, currentDualsOfMachines, bestDualsOfMachines, currentDualsOfLmSRCs, bestDualsOfLmSRCs);
                            // Adjust alpha objValue
                            alpha = dualPriceSmoothing.AdjustAlphaValue(alpha, angle);
                        }
                    }
                    // Column generation iteration termination
                    if (flag.Sum() == instance.numMachines) break;
                }
                else
                {
                    solution._isFeasible = false;
                    break;
                }
                iteration++;
            }

            // Check whether the ys is integer or not
            solution.CheckIntegerality();

            solutionInfo.numOfCGIterations += iteration;
        }
        /// <summary>
        /// Select Partial New Labels
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="newLabelsOnSingleMachine"></param>
        /// <param name="solution"></param>
        /// <param name="usedPartialScheduleSet"></param>
        public List<ForwardLabel> SelectNewLabels(Parameters parameters, List<ForwardLabel> newLabelsOnSingleMachine, List<PartialSchedule> usedPartialScheduleSet) 
        {
            return newLabelsOnSingleMachine
                .Where(label => !CheckIfLabelExists(label, usedPartialScheduleSet))
                .Take(parameters.numAddedColumns)
                .ToList();
        }

        public List<BackwardLabel> SelectNewBackwardLabels(Parameters parameters, List<BackwardLabel> newLabelsOnSingleMachine, List<PartialSchedule> usedPartialScheduleSet)
        {
            return newLabelsOnSingleMachine
                .Where(label => !CheckIfBackwardLabelExists(label, usedPartialScheduleSet))
                .Take(parameters.numAddedColumns)
                .ToList();
        }

        /// <summary>
        ///  Convert labelSet to partial schedule
        /// </summary>
        /// <param name="machineID"></param>
        /// <param name="numJobs"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="label"></param>
        /// <param name="numAddColumn"></param>
        /// <returns></returns>
        public List<PartialSchedule> ConvertLabelsToPartialSchedule(int machineID, int numJobs, double[,] processingTimes, int[] weights, List<ForwardLabel> labelSet)
        {
            List<PartialSchedule> newPartialSchedulesOnSingleMachines = new List<PartialSchedule>();

            for (int l = 0; l < labelSet.Count; l++)
            {
                PartialSchedule partialScheduleOnSingleMachine = new PartialSchedule();
                partialScheduleOnSingleMachine.setOfProcessedJobs = labelSet[l].setOfProcessedJobs;
                partialScheduleOnSingleMachine.TWCT = 0;
                partialScheduleOnSingleMachine.vectorOfProcessedJob = new double[numJobs];
                partialScheduleOnSingleMachine.time = 0;
                for (int m = 0; m < partialScheduleOnSingleMachine.setOfProcessedJobs.Count; m++)
                {
                    partialScheduleOnSingleMachine.time += processingTimes[partialScheduleOnSingleMachine.setOfProcessedJobs[m] - 1, machineID];
                    partialScheduleOnSingleMachine.TWCT += weights[partialScheduleOnSingleMachine.setOfProcessedJobs[m] - 1] * partialScheduleOnSingleMachine.time;
                    partialScheduleOnSingleMachine.vectorOfProcessedJob[partialScheduleOnSingleMachine.setOfProcessedJobs[m] - 1] = 1;
                }
                newPartialSchedulesOnSingleMachines.Add(partialScheduleOnSingleMachine);
            }
            return newPartialSchedulesOnSingleMachines;
        }
        public List<PartialSchedule> ConvertBackwardLabelsToPartialSchedule(int machineID, int numJobs, double[,] processingTimes, int[] weights, List<BackwardLabel> labelSet)
        {
            List<PartialSchedule> newPartialSchedulesOnSingleMachines = new List<PartialSchedule>();

            for (int l = 0; l < labelSet.Count; l++)
            {
                PartialSchedule partialScheduleOnSingleMachine = new PartialSchedule();
                partialScheduleOnSingleMachine.setOfProcessedJobs = labelSet[l].setOfProcessedJobs;
                partialScheduleOnSingleMachine.TWCT = 0;
                partialScheduleOnSingleMachine.vectorOfProcessedJob = new double[numJobs];
                partialScheduleOnSingleMachine.time = 0;
                for (int m = 0; m < partialScheduleOnSingleMachine.setOfProcessedJobs.Count; m++)
                {
                    partialScheduleOnSingleMachine.time += processingTimes[partialScheduleOnSingleMachine.setOfProcessedJobs[m] - 1, machineID];
                    partialScheduleOnSingleMachine.TWCT += weights[partialScheduleOnSingleMachine.setOfProcessedJobs[m] - 1] * partialScheduleOnSingleMachine.time;
                    partialScheduleOnSingleMachine.vectorOfProcessedJob[partialScheduleOnSingleMachine.setOfProcessedJobs[m] - 1] = 1;
                }
                newPartialSchedulesOnSingleMachines.Add(partialScheduleOnSingleMachine);
            }
            return newPartialSchedulesOnSingleMachines;
        }

        /// <summary>
        /// Check if label exists
        /// </summary>
        /// <param name="label"></param>
        /// <param name="partialSchedules"></param>
        /// <returns></returns>
        public bool CheckIfLabelExists(ForwardLabel label, List<PartialSchedule> partialSchedules)
        {
            bool isLabelExists = false;
            Auxiliary auxiliary = new Auxiliary();

            foreach (PartialSchedule partialSchedule in partialSchedules)
            {
                if (auxiliary.CheckIfListsEqual(partialSchedule.setOfProcessedJobs, label.setOfProcessedJobs))
                {
                    isLabelExists = true;
                    break;
                }
            }
            return isLabelExists;
        }
        public bool CheckIfBackwardLabelExists(BackwardLabel label, List<PartialSchedule> partialSchedules)
        {
            bool isLabelExists = false;
            Auxiliary auxiliary = new Auxiliary();

            foreach (PartialSchedule partialSchedule in partialSchedules)
            {
                if (auxiliary.CheckIfListsEqual(partialSchedule.setOfProcessedJobs, label.setOfProcessedJobs))
                {
                    isLabelExists = true;
                    break;
                }
            }
            return isLabelExists;
        }
        /// <summary>
        /// Calculate Threshold
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="processingTimes"></param>
        /// <returns></returns>
        public double CalculateThreshold(int numMachines, int numJobs, double[,] processingTimes)
        {
            double threshold = 0;
            for (int j = 0; j < numJobs; j++)
            {
                double minProcessingTime = double.MaxValue;
                double maxProcessingTime = double.MinValue;
                for (int k = 0; k < numMachines; k++)
                {
                    if (processingTimes[j, k] < minProcessingTime) minProcessingTime = processingTimes[j, k];
                    if (processingTimes[j, k] > maxProcessingTime) maxProcessingTime = processingTimes[j, k];
                }
                threshold += minProcessingTime + maxProcessingTime;
            }
            threshold = threshold / (2 * numJobs);
            return threshold;
        }
    }
    public class DualPriceSmoothing
    {
        /// <summary>
        ///  Calculate Lagrangian Relaxation Bound
        /// </summary>
        /// <param name="dualsOfJobs"></param>
        /// <param name="doualsOfMachines"></param>
        /// <returns></returns>
        public double CalculateLagrangianRelaxationBound(double[] dualsOfJobs, double[] doualsOfMachines, List<double> dualsOfLmSRCs, double[] bestReducedCost)
        {
            double bound = 0;
            bound += dualsOfJobs.Sum() + doualsOfMachines.Sum() + dualsOfLmSRCs.Sum() + bestReducedCost.Sum();
            return bound;
        }
        /// <summary>
        /// Calculate angle
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="numJobs"></param>
        /// <param name="numMachines"></param>
        /// <param name="currentDualsOfJobs"></param>
        /// <param name="bestDualsOfJobs"></param>
        /// <param name="currentDualsOfMachines"></param>
        /// <param name="bestDualsOfMachines"></param>
        /// <returns></returns>
        public double CalculateAngle(Solution solution, int numJobs, int numMachines, double[] currentDualsOfJobs, double[] bestDualsOfJobs, double[] currentDualsOfMachines, double[] bestDualsOfMachines, List<double> currentDualsOfLmSRCs, List<double> bestDualsOfLmSRCs)
        {
            // Calculate sub-gradient based on  job
            double[] subGradientOnJobs = new double[numJobs];
            for (int j = 0; j < numJobs; j++)
            {
                subGradientOnJobs[j] = 1;
                for (int k = 0; k < solution.yks.Count; k++)
                {
                    for (int s = 0; s < solution.yks[k].Length; s++)
                    {
                        subGradientOnJobs[j] -= solution.yks[k][s] * solution.usedPartialScheduleSet[k][s].vectorOfProcessedJob[j];
                    }
                }
            }
            // Calculate sub-gradient based on machine
            double[] subGradientOnMachines = new double[numMachines];
            for (int k = 0; k < numMachines; k++)
            {
                subGradientOnMachines[k] = 1;
                for (int s = 0; s < solution.yks[k].Length; s++)
                {
                    subGradientOnMachines[k] -= solution.yks[k][s];
                }
            }

            // Calculate sub-gradient based on 3-subset
            List<double> subGradientOnSRCs = new List<double>();
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                subGradientOnSRCs.Add(1);
                for (int k = 0; k < numMachines; k++)
                {
                    for (int s = 0; s < solution.yks[k].Length; s++)
                    {
                        if (solution.yks[k][s] == 0) continue;
                        subGradientOnSRCs[o] -= solution.yks[k][s] * solution.usedLmSRCs[o].coeff[k][s];
                    }
                }
            }

            // Calculate angle
            double angle = 0;
            for (int j = 0; j < numJobs; j++)
            {
                angle += subGradientOnJobs[j] * (currentDualsOfJobs[j] - bestDualsOfJobs[j]);
            }
            for (int k = 0; k < numMachines; k++)
            {
                angle += subGradientOnMachines[k] * (currentDualsOfMachines[k] - bestDualsOfMachines[k]);
            }
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                angle += subGradientOnSRCs[o] * (currentDualsOfLmSRCs[o] - bestDualsOfLmSRCs[o]);
            }
            return angle;
        }
        /// <summary>
        /// Adjust alpha objValue
        /// </summary>
        /// <param name="alpha"></param>
        /// <param name="angle"></param>
        /// <returns></returns>
        public double AdjustAlphaValue(double alpha, double angle)
        {
            if (angle > 0)
            {
                // Increase alpha objValue
                alpha = alpha + (1 - alpha) * 0.1;
            }
            else
            {
                //Decrease alpha
                alpha = alpha - 0.1;
                if (alpha < 0)
                {
                    alpha = 0;
                }
            }
            return alpha;
        }

    }
    public class RowGeneration
    {
        /// <summary>
        ///  Produce lm-SRCs
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="scheduleSets"></param>
        /// <param name="Yks"></param>
        /// <returns></returns>
        public List<LmSRCOfVertex> ProduceLmSRCs(int numMachines, List<List<int>> subSets, List<List<PartialSchedule>> scheduleSets, List<double[]> Yks)
        {
            List<LmSRCOfVertex> lmSRCs = new List<LmSRCOfVertex>();
            for (int o = 0; o < subSets.Count; o++)
            {
                //------ Check if the current SRC is satisfied ------
                double sum = 0;
                for (int k = 0; k < numMachines; k++)
                {
                    for (int s = 0; s < scheduleSets[k].Count; s++)
                    {
                        if (Yks[k][s] == 0) continue;
                        List<int> intersationSet = scheduleSets[k][s].setOfProcessedJobs.Intersect(subSets[o]).ToList();
                        if (intersationSet.Count > 1)
                        {
                            sum += Yks[k][s];
                        }
                    }
                }

                // The current SRC is satisfied
                if (sum < 1.000001)
                {
                    continue;
                }

                LmSRCOfVertex lmSRC = new LmSRCOfVertex();
                lmSRC.id = o;
                lmSRC.violation = sum - 1;
                List<List<double>> coeffSet = new List<List<double>>();
                List<List<int>> memorySet = new List<List<int>>();
                for (int k = 0; k < numMachines; k++)
                {
                    List<int> memorySetOnSingleMachine = ProceduceMemorySet(scheduleSets[k], subSets[o], Yks[k], 0.5);
                    memorySet.Add(memorySetOnSingleMachine);
                    List<double> coeffSetOnSingleMachine = new List<double>();
                    for (int s = 0; s < scheduleSets[k].Count; s++)
                    {
                        double coeff = CalculateCoefficientOfLmSRC(scheduleSets[k][s], memorySetOnSingleMachine, subSets[o], 0.5);
                        coeffSetOnSingleMachine.Add(coeff);
                    }
                    coeffSet.Add(coeffSetOnSingleMachine);
                }
                //lmSRC.dualValue = 0;
                lmSRC.coeff = new List<List<double>>(coeffSet);
                lmSRC.memorySet = new List<List<int>>(memorySet);
                lmSRC.subSet = new List<int>(subSets[o]);
                lmSRCs.Add(lmSRC);
            }
            return lmSRCs;
        }
        /// <summary>
        /// Get 3-jobs Sub-sets
        /// </summary>
        /// <param name="numJobs"></param>
        /// <returns></returns>
        public List<List<int>> GetSubSets(int numJobs)
        {
            List<List<int>> subSets = new List<List<int>>();
            for (int i = 1; i <= numJobs - 2; i++)
            {
                for (int j = i + 1; j <= numJobs - 1; j++)
                {
                    for (int k = j + 1; k <= numJobs; k++)
                    {
                        List<int> currentSubSet = new List<int> { i, j, k };
                        subSets.Add(currentSubSet);
                    }
                }
            }
            return subSets;
        }
        /// <summary>
        /// Proceduce Memory Set on Machine machineID
        /// </summary>
        /// <param name="schedule"></param>
        /// <param name="subSet"></param>
        /// <param name="solution"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        public List<int> ProceduceMemorySet(List<PartialSchedule> schedule, List<int> subSet, double[] solution, double p)
        {
            List<int> memorySet = new List<int>();
            memorySet.AddRange(subSet);

            for (int s = 0; s < schedule.Count; s++)
            {
                if ((solution[s] > 0) && (schedule[s].setOfProcessedJobs.Intersect(subSet).ToList().Count > 0))
                {
                    double state = 0;
                    List<int> aux = new List<int>();

                    for (int j = 0; j < schedule[s].setOfProcessedJobs.Count; j++)
                    {
                        int currentJob = schedule[s].setOfProcessedJobs[j];
                        List<int> tempJobSet = new List<int>() { currentJob };
                        if (subSet.Contains(currentJob))
                        {
                            state += p;
                            if (state >= 1)
                            {
                                memorySet = memorySet.Union(aux).ToList();
                                aux.Clear();
                                state -= 1;
                            }
                        }
                        else if (state > 0)
                        {
                            aux = aux.Union(tempJobSet).ToList();
                        }
                    }
                }
            }
            return memorySet;
        }
        /// <summary>
        ///  Calculate Coefficient of lm-SRC on Partial Shcedule s on Machine machineID 
        /// </summary>
        /// <param name="schedule"></param>
        /// <param name="memorySet"></param>
        /// <param name="subSet"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        public double CalculateCoefficientOfLmSRC(PartialSchedule schedule, List<int> memorySet, List<int> subSet, double p)
        {
            double coeff = 0;
            double state = 0;

            for (int j = 0; j < schedule.setOfProcessedJobs.Count; j++)
            {
                int job = schedule.setOfProcessedJobs[j];
                if (!memorySet.Contains(job))
                {
                    state = 0;
                }
                else if (subSet.Contains(job))
                {
                    state += p;
                    if (state >= 1)
                    {
                        coeff += 1;
                        state -= 1;
                    }
                }
            }
            return coeff;
        }
    }
    public class PSPSolver
    {
        /// <summary>
        /// Mono-directional nextLabel setting algorithm
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="directions"></param>
        /// <param name="machineID"></param>
        /// <param name="numJobs"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <returns></returns>
        public BucketGraph ForwardLabelAlgorithm(Solution solution, string directions, int machineID, double threshold, UPMSPInstances instance, Parameters parameters, Switcher switcher)
        {
            // Initialize the bucket graph
            BucketGraph bucketGraph = new BucketGraph();
            bucketGraph = solution.forwardBucketGraphs[machineID];
          
            bucketGraph.nonDominatedLabelsSet = new List<ForwardLabel>();
            foreach (Bucket temBucket in bucketGraph.buckets)
            {
                temBucket.labelSet = new List<ForwardLabel>();
                temBucket.minReducedCost = double.MaxValue;
            }

            // Obtain the initial nextLabel 
            ForwardLabel initialLabel = new ForwardLabel();
            initialLabel.reducedCost = -solution.dualsOfMachines[machineID];
            initialLabel.lastJob = 0;
            initialLabel.time = 0;
            initialLabel.setOfProcessedJobs = new List<int>();
            initialLabel.setOfBucketIndex = new List<int>();
            initialLabel.lmSRCsState = new List<double>();
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                initialLabel.lmSRCsState.Add(0);
            }
            initialLabel._isExtended = false;

            // Add the initial nextLabel to the initial bucket
            bucketGraph.stronglyConnectedComponents[0].bucket.labelSet.Add(initialLabel);

            // Extend and dominate the nextLabel
            foreach (StronglyConnectedComponent stronglyConnectedComponents in bucketGraph.stronglyConnectedComponents)
            {
                Bucket bucket = stronglyConnectedComponents.bucket;

                foreach (ForwardLabel label in bucket.labelSet)
                {
                    if (label._isExtended == true) continue;
                    List<Bucket> visitedBuckets = new List<Bucket>();

                    // Check that the nextLabel is not dominated by the labelSet in the comp-wise smaller jumpBuckets.
                    if (!DominatedInCompWiseSmallerBuckets(label, bucket, bucket, visitedBuckets, bucketGraph, solution.dualsOfLmSRCsOfVertex, directions))
                    {
                        foreach (BucketArc bucketArc in bucketGraph.adjListOfBucketArcs[bucket])
                        {
                            ForwardLabel newLabel = new ForwardLabel();
                            // The nextLabel is extended along the tempBucket currentBucketArc;
                            // The new labelSet may be contained in other than the head tempBucket, and flag = false.
                            if (Extend(label, bucketArc, newLabel, machineID, instance.weights, solution.dualsOfJobs, solution.dualsOfLmSRCsOfVertex, bucketGraph.dynamicShrinkBound[bucketArc.headBucket.vertex], threshold, solution.usedLmSRCs, directions))
                            {
                                // Find the bucket that may contain the new label
                                Bucket newBucket = new Bucket();
                                if (newLabel.time > bucketArc.headBucket.ub)
                                {
                                    int index = 1 + parameters.numOfBucketOnOneVertex * (bucketArc.headBucket.vertex - 1) + bucketArc.headBucket.index;
                                    for (int b = 1; b < parameters.numOfBucketOnOneVertex - bucketArc.headBucket.index; b++)
                                    {
                                        if ((bucketGraph.buckets[index + b].lb < newLabel.time) && (newLabel.time <= bucketGraph.buckets[index + b].ub))
                                        {
                                            newBucket = bucketGraph.buckets[index + b];
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    newBucket = bucketArc.headBucket;
                                }

                                // Check that the new nextLabel is not dominated by the labelSet in the head (new) tempBucket
                                if (!DominatedByLabelsInBucket(newLabel, newBucket, solution.dualsOfLmSRCsOfVertex, directions))
                                {
                                    newLabel._isExtended = false;
                                    // Add the new nextLabel into the head (new) tempBucket
                                    newBucket.labelSet.Add(newLabel);
                                    // Remove the dominated labelSet in the head (new) tempBucket
                                    newBucket.RemoveDominatedLabelInBucket(newLabel, solution.dualsOfLmSRCsOfVertex, directions);
                                }
                            }
                        }
                    }
                    label._isExtended = true;
                }

                // Update the reduced cost of jumpBuckets
                for (int l = 0; l < bucket.labelSet.Count; l++)
                {
                    if (bucket.minReducedCost > bucket.labelSet[l].reducedCost)
                    {
                        bucket.minReducedCost = bucket.labelSet[l].reducedCost;
                    }
                    //bucketGraph.nonDominatedLabelsSet.Add(bucket.labelSet[l]);
                }

                List<Bucket> adjComponentWiseSmallerBuckets = bucketGraph.adjComponentWiseSmallerBuckets[bucket].ToList();
                for (int b = 0; b < adjComponentWiseSmallerBuckets.Count; b++)
                {
                    if (bucket.minReducedCost > adjComponentWiseSmallerBuckets[b].minReducedCost)
                    {
                        bucket.minReducedCost = adjComponentWiseSmallerBuckets[b].minReducedCost;
                    }
                }
            }
            bucketGraph.nonDominatedLabelsSet = new List<ForwardLabel>(bucketGraph.buckets.Last().labelSet);
            bucketGraph.nonDominatedLabelsSet = bucketGraph.nonDominatedLabelsSet.OrderBy(o => o.reducedCost).ToList();
            //Console.WriteLine("Machine  " + machineID + " num of ND labels " + bucketGraph.nonDominatedLabelsSet.Count);
            return bucketGraph;
        }
        public BucketGraph BackwardLabelAlgorithm(Solution solution, string directions, int machineID, double threshold, UPMSPInstances instance, Parameters parameters, Switcher switcher)
        {
            // Initialize the bucket graph
            BucketGraph bucketGraph = new BucketGraph();
            bucketGraph = solution.backwardBucketGraphs[machineID];

            bucketGraph.nonDominatedBackwardLabelsSet = new List<BackwardLabel>();
            foreach (Bucket temBucket in bucketGraph.buckets)
            {
                temBucket.backwardLabelSet = new List<BackwardLabel>();
                temBucket.minReducedCost = double.MaxValue;
            }

            // Obtain the initial nextLabel 
            BackwardLabel initialLabel = new BackwardLabel();
            initialLabel.firstJob = instance.numJobs + 1;
            initialLabel.duration = 0;
            initialLabel.time = bucketGraph.dynamicShrinkBound.Last().upperBound;
            initialLabel.cumulativeWeight = 0;
            //initialLabel.baseReducedCost = -solution.dualsOfMachines[machineID];
            initialLabel.baseReducedCost = 0; 
            initialLabel.setOfProcessedJobs = new List<int>();
            initialLabel.setOfBucketIndex = new List<int>();
            initialLabel.lmSRCsState = new List<double>();
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                initialLabel.lmSRCsState.Add(0);
            }
            initialLabel._isExtended = false;

            // Add the initial nextLabel to the initial bucket
            bucketGraph.stronglyConnectedComponents[0].bucket.backwardLabelSet.Add(initialLabel);

            // Extend and dominate the nextLabel
            foreach (StronglyConnectedComponent stronglyConnectedComponents in bucketGraph.stronglyConnectedComponents)
            {
                Bucket bucket = stronglyConnectedComponents.bucket;

                foreach (BackwardLabel label in bucket.backwardLabelSet)
                {
                    if (label._isExtended == true) continue;
                    List<Bucket> visitedBuckets = new List<Bucket>();

                    // Check that the nextLabel is not dominated by the labelSet in the comp-wise smaller jumpBuckets.
                    if (!BackwardDominatedInCompWiseSmallerBuckets(label, bucket, bucket, visitedBuckets, bucketGraph, solution.dualsOfLmSRCsOfVertex, directions))
                    {
                        foreach (BucketArc bucketArc in bucketGraph.adjListOfBucketArcs[bucket])
                        {
                            BackwardLabel newLabel = new BackwardLabel();
                            // The nextLabel is extended along the tempBucket currentBucketArc;
                            // The new labelSet may be contained in other than the head tempBucket, and flag = false.
                            if (BackwardExtend(label, bucketArc, newLabel, machineID, instance.weights, solution.dualsOfJobs, solution.dualsOfLmSRCsOfVertex, bucketGraph.dynamicShrinkBound[bucketArc.headBucket.vertex], threshold, solution.usedLmSRCs, directions))
                            {
                                // Find the bucket that may contain the new label
                                Bucket newBucket = new Bucket();
                                if ((newLabel.time < bucketArc.headBucket.lb))
                                {
                                    int index = 1 + parameters.numOfBucketOnOneVertex * (bucketArc.headBucket.vertex - 1) + bucketArc.headBucket.index;
                                    for (int b = 1; b < parameters.numOfBucketOnOneVertex - bucketArc.headBucket.index; b++)
                                    {
                                        if ((bucketGraph.buckets[index + b].ub > newLabel.time) && (newLabel.time >= bucketGraph.buckets[index + b].lb))
                                        {
                                            newBucket = bucketGraph.buckets[index + b];
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    newBucket = bucketArc.headBucket;
                                }

                                // Check that the new nextLabel is not dominated by the labelSet in the head (new) tempBucket
                                if (!BackwardDominatedByLabelsInBucket(newLabel, newBucket, solution.dualsOfLmSRCsOfVertex, directions))
                                {
                                    newLabel._isExtended = false;
                                    // Add the new nextLabel into the head (new) tempBucket
                                    newBucket.backwardLabelSet.Add(newLabel);
                                    // Remove the dominated labelSet in the head (new) tempBucket
                                    newBucket.RemoveDominatedBackwardLabelInBucket(newLabel, solution.dualsOfLmSRCsOfVertex, directions);
                                }
                            }
                        }
                    }
                    label._isExtended = true;
                }

                // Update the reduced cost of jumpBuckets
                for (int l = 0; l < bucket.backwardLabelSet.Count; l++)
                {
                    if (bucket.minReducedCost > bucket.backwardLabelSet[l].baseReducedCost)
                    {
                        bucket.minReducedCost = bucket.backwardLabelSet[l].baseReducedCost;
                    }
                    //bucketGraph.nonDominatedLabelsSet.Add(bucket.labelSet[l]);
                }

                List<Bucket> adjComponentWiseSmallerBuckets = bucketGraph.adjComponentWiseSmallerBuckets[bucket].ToList();
                for (int b = 0; b < adjComponentWiseSmallerBuckets.Count; b++)
                {
                    if (bucket.minReducedCost > adjComponentWiseSmallerBuckets[b].minReducedCost)
                    {
                        bucket.minReducedCost = adjComponentWiseSmallerBuckets[b].minReducedCost;
                    }
                }
            }
            bucketGraph.nonDominatedBackwardLabelsSet = new List<BackwardLabel>(bucketGraph.buckets.First().backwardLabelSet);
            bucketGraph.nonDominatedBackwardLabelsSet = bucketGraph.nonDominatedBackwardLabelsSet.OrderBy(o => o.baseReducedCost).ToList();
            //Console.WriteLine("Machine  " + machineID + " num of ND labels " + bucketGraph.nonDominatedLabelsSet.Count);
            return bucketGraph;
        }
        public BucketGraph BackwardLabelAlgorithmWithCompletionBound(Solution solution, string directions, int machineID, double threshold, double gap, Dictionary<int, (double MinReducedCost, double MinTime)> forwardValues, UPMSPInstances instance, Parameters parameters, Switcher switcher)
        {
            // Initialize the bucket graph
            BucketGraph bucketGraph = new BucketGraph();
            bucketGraph = solution.backwardBucketGraphs[machineID];

            bucketGraph.nonDominatedBackwardLabelsSet = new List<BackwardLabel>();
            foreach (Bucket temBucket in bucketGraph.buckets)
            {
                temBucket.backwardLabelSet = new List<BackwardLabel>();
                temBucket.minReducedCost = double.MaxValue;
            }

            // Obtain the initial nextLabel 
            BackwardLabel initialLabel = new BackwardLabel();
            initialLabel.firstJob = instance.numJobs + 1;
            initialLabel.duration = 0;
            initialLabel.time = bucketGraph.dynamicShrinkBound.Last().upperBound;
            initialLabel.cumulativeWeight = 0;
            //initialLabel.baseReducedCost = -solution.dualsOfMachines[machineID];
            initialLabel.baseReducedCost = 0;
            initialLabel.setOfProcessedJobs = new List<int>();
            initialLabel.setOfBucketIndex = new List<int>();
            initialLabel.lmSRCsState = new List<double>();
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                initialLabel.lmSRCsState.Add(0);
            }
            initialLabel._isExtended = false;

            // Add the initial nextLabel to the initial bucket
            bucketGraph.stronglyConnectedComponents[0].bucket.backwardLabelSet.Add(initialLabel);

            // Extend and dominate the nextLabel
            foreach (StronglyConnectedComponent stronglyConnectedComponents in bucketGraph.stronglyConnectedComponents)
            {
                Bucket bucket = stronglyConnectedComponents.bucket;

                foreach (BackwardLabel label in bucket.backwardLabelSet)
                {
                    if (label._isExtended == true) continue;
                    List<Bucket> visitedBuckets = new List<Bucket>();

                    // Check that the nextLabel is not dominated by the labelSet in the comp-wise smaller jumpBuckets.
                    if (!BackwardDominatedInCompWiseSmallerBuckets(label, bucket, bucket, visitedBuckets, bucketGraph, solution.dualsOfLmSRCsOfVertex, directions))
                    {
                        foreach (BucketArc bucketArc in bucketGraph.adjListOfBucketArcs[bucket])
                        {
                            BackwardLabel newLabel = new BackwardLabel();
                            // The nextLabel is extended along the tempBucket currentBucketArc;
                            // The new labelSet may be contained in other than the head tempBucket, and flag = false.
                            if (BackwardExtendWithCompletionBound(label, bucketArc, newLabel, machineID, instance.weights, solution.dualsOfJobs, solution.dualsOfLmSRCsOfVertex, bucketGraph.dynamicShrinkBound[bucketArc.headBucket.vertex], threshold, solution.usedLmSRCs, gap, forwardValues, directions))
                            {
                                // Find the bucket that may contain the new label
                                Bucket newBucket = new Bucket();
                                if ((newLabel.time < bucketArc.headBucket.lb))
                                {
                                    int index = 1 + parameters.numOfBucketOnOneVertex * (bucketArc.headBucket.vertex - 1) + bucketArc.headBucket.index;
                                    for (int b = 1; b < parameters.numOfBucketOnOneVertex - bucketArc.headBucket.index; b++)
                                    {
                                        if ((bucketGraph.buckets[index + b].ub > newLabel.time) && (newLabel.time >= bucketGraph.buckets[index + b].lb))
                                        {
                                            newBucket = bucketGraph.buckets[index + b];
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    newBucket = bucketArc.headBucket;
                                }

                                // Check that the new nextLabel is not dominated by the labelSet in the head (new) tempBucket
                                if (!BackwardDominatedByLabelsInBucket(newLabel, newBucket, solution.dualsOfLmSRCsOfVertex, directions))
                                {
                                    newLabel._isExtended = false;
                                    // Add the new nextLabel into the head (new) tempBucket
                                    newBucket.backwardLabelSet.Add(newLabel);
                                    // Remove the dominated labelSet in the head (new) tempBucket
                                    newBucket.RemoveDominatedBackwardLabelInBucket(newLabel, solution.dualsOfLmSRCsOfVertex, directions);
                                }
                            }
                        }
                    }
                    label._isExtended = true;
                }

                // Update the reduced cost of jumpBuckets
                for (int l = 0; l < bucket.backwardLabelSet.Count; l++)
                {
                    if (bucket.minReducedCost > bucket.backwardLabelSet[l].baseReducedCost)
                    {
                        bucket.minReducedCost = bucket.backwardLabelSet[l].baseReducedCost;
                    }
                    //bucketGraph.nonDominatedLabelsSet.Add(bucket.labelSet[l]);
                }

                List<Bucket> adjComponentWiseSmallerBuckets = bucketGraph.adjComponentWiseSmallerBuckets[bucket].ToList();
                for (int b = 0; b < adjComponentWiseSmallerBuckets.Count; b++)
                {
                    if (bucket.minReducedCost > adjComponentWiseSmallerBuckets[b].minReducedCost)
                    {
                        bucket.minReducedCost = adjComponentWiseSmallerBuckets[b].minReducedCost;
                    }
                }
            }
            bucketGraph.nonDominatedBackwardLabelsSet = new List<BackwardLabel>(bucketGraph.buckets.First().backwardLabelSet);
            bucketGraph.nonDominatedBackwardLabelsSet = bucketGraph.nonDominatedBackwardLabelsSet.OrderBy(o => o.baseReducedCost).ToList();
            //Console.WriteLine("Machine  " + machineID + " num of ND labels " + bucketGraph.nonDominatedLabelsSet.Count);
            return bucketGraph;
        }
        /// <summary>
        ///   Bi-directional nextLabel algorithm
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="numJobs"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <returns></returns>
        public List<ForwardLabel> BiDirectionalLabelAlgorithm(Solution solution, int machineID, double forwardThreshold, double backwardThreshold, UPMSPInstances instance, Parameters parameters, Switcher switcher)
        {
            Solution currentSolution = solution;
            BucketGraph forwardBucketGraph = currentSolution.forwardBucketGraphs[machineID];
            BucketGraph backwardBucketGraph = currentSolution.backwardBucketGraphs[machineID];
            forwardBucketGraph = ForwardLabelAlgorithm(currentSolution, "Forward", machineID, forwardThreshold, instance, parameters, switcher);
            backwardBucketGraph = BackwardLabelAlgorithm(currentSolution, "Backward", machineID, backwardThreshold, instance, parameters, switcher);

            // Obtain the processing time of machine machineID
            double[] processingTimes = new double[instance.numJobs];
            for (int j = 0; j < instance.numJobs; j++)
            {
                processingTimes[j] = instance.processingTimes[j, machineID];
            }

            List<ForwardLabel> nonDominatedLabelsSet = new List<ForwardLabel>();
            foreach (Bucket bucket in forwardBucketGraph.buckets)
            {
                if (bucket.labelSet.Count == 0) continue;
                foreach (ForwardLabel forwardLabel in bucket.labelSet)
                {
                    if (forwardBucketGraph.adjListOfBucketArcs[bucket].Count == 0) continue;
                    foreach (BucketArc bucketArc in forwardBucketGraph.adjListOfBucketArcs[bucket])
                    {
                        int headVertex = bucketArc.headBucket.vertex;
                        foreach (Bucket b in backwardBucketGraph.buckets)
                        {
                            if ((b.vertex == headVertex) && (b.ub >= forwardLabel.time))
                            {
                                foreach (BackwardLabel backwardLabel in b.backwardLabelSet)
                                {
                                    ForwardLabel completeLabel = new ForwardLabel();
                                    completeLabel._isExtended = true;
                                    completeLabel.setOfProcessedJobs.AddRange(new List<int>(forwardLabel.setOfProcessedJobs));
                                    for (int i = backwardLabel.setOfProcessedJobs.Count - 1; i >= 0; i--)
                                    {
                                        completeLabel.setOfProcessedJobs.Add(backwardLabel.setOfProcessedJobs[i]);
                                    }
                                    completeLabel.lastJob = instance.numJobs + 1;
                                    completeLabel.reducedCost = forwardLabel.reducedCost + backwardLabel.baseReducedCost + forwardLabel.time * backwardLabel.cumulativeWeight;
                                    for (int o = 0; o < currentSolution.dualsOfLmSRCsOfVertex.Count; o++)
                                    {
                                        if (forwardLabel.lmSRCsState[o] + backwardLabel.lmSRCsState[o] >= 1)
                                        {
                                            completeLabel.reducedCost -= currentSolution.dualsOfLmSRCsOfVertex[o];
                                        }
                                    }
                                    nonDominatedLabelsSet.Add(completeLabel);
                                }
                            }
                        }
                    }
                }
            }
            nonDominatedLabelsSet = nonDominatedLabelsSet.Where(x => x.reducedCost < -0.001).ToList();
            nonDominatedLabelsSet = nonDominatedLabelsSet.OrderBy(x => x.reducedCost).ToList();
            return nonDominatedLabelsSet;
        }
        /// <summary>
        /// Extend
        /// </summary>
        /// <param name="nonExtendedLabel"></param>
        /// <param name="bucket"></param>
        /// <param name="bucketArc"></param>
        /// <param name="newLabel"></param>
        /// <param name="machineID"></param>
        /// <param name="weights"></param>
        /// <param name="dualsOfJobs"></param>
        /// <param name="threshold"></param>
        /// <param name="lmSRCs"></param>
        /// <param name="directions"></param>
        /// <returns></returns>
        public bool Extend(ForwardLabel nonExtendedLabel, BucketArc bucketArc, ForwardLabel newLabel, int machineID, int[] weights, double[] dualsOfJobs, List<double> dualsOfLmSRCs, DynamicShrinkBound boundOnHeadVertex, double threshold, List<LmSRCOfVertex> lmSRCs, string directions)
        {
            bool flag = false;
            // Update the current job
            newLabel.lastJob = bucketArc.arc[1];
            // Update the current completeTime
            // The time consumptions on the bucket currentBucketArc of  the forward nextLabel and the backward nextLabel are same
            newLabel.time = nonExtendedLabel.time;
            newLabel.reducedCost = nonExtendedLabel.reducedCost;
            newLabel.lmSRCsState = new List<double>(nonExtendedLabel.lmSRCsState);

            newLabel.time += bucketArc.resourceConsumption;
            if (newLabel.time > boundOnHeadVertex.upperBound || newLabel.time > threshold)
            {
                return flag;
            }

            // Update the set of processed jobs. Job 0 and Job numOfJob+1 are the begin and end flags
            newLabel.setOfProcessedJobs = new List<int>(nonExtendedLabel.setOfProcessedJobs);
            newLabel.setOfBucketIndex = new List<int>(nonExtendedLabel.setOfBucketIndex);
            if ((newLabel.lastJob != weights.Length + 1) && (newLabel.lastJob != 0))
            {
                newLabel.setOfProcessedJobs.Add(newLabel.lastJob);
                newLabel.setOfBucketIndex.Add(bucketArc.headBucket.index);
            }

            // Update the reduced cost
            // The reduced cost on the bucket currentBucketArc of the forward nextLabel and the backward nextLabel are same
            double weight = 0;
            double dualOfJobs = 0;
            double completeTime = 0;

            if ((newLabel.lastJob != weights.Length + 1))
            {
                weight = weights[newLabel.lastJob - 1];
                dualOfJobs = dualsOfJobs[newLabel.lastJob - 1];
                completeTime = newLabel.time;
            }
            newLabel.reducedCost += weight * completeTime - dualOfJobs;

            // Update the set of lmSRCs

            for (int o = 0; o < lmSRCs.Count; o++)
            {
                if (dualsOfLmSRCs[o] >= 0) continue;
                if (!lmSRCs[o].memorySet[machineID].Contains(newLabel.lastJob))
                {
                    newLabel.lmSRCsState[o] = 0;
                }
                else
                {
                    if (lmSRCs[o].subSet.Contains(newLabel.lastJob))
                    {
                        newLabel.lmSRCsState[o] += 0.5;
                        if (newLabel.lmSRCsState[o] >= 1)
                        {
                            newLabel.lmSRCsState[o] -= 1;
                            newLabel.reducedCost -= dualsOfLmSRCs[o];
                        }
                    }
                }
            }
            flag = true;
            return flag;
        }
        public bool BackwardExtend(BackwardLabel nonExtendedLabel, BucketArc bucketArc, BackwardLabel newLabel, int machineID, int[] weights, double[] dualsOfJobs, List<double> dualsOfLmSRCs, DynamicShrinkBound boundOnHeadVertex, double threshold, List<LmSRCOfVertex> lmSRCs, string directions)
        {
            bool flag = false;
            // Update the current job
            newLabel.firstJob = bucketArc.arc[1];
            // Update the current completeTime
            // The time consumptions on the bucket currentBucketArc of  the forward nextLabel and the backward nextLabel are same
            newLabel.duration = nonExtendedLabel.duration;
            newLabel.time = nonExtendedLabel.time;
            newLabel.cumulativeWeight = nonExtendedLabel.cumulativeWeight;
            newLabel.baseReducedCost = nonExtendedLabel.baseReducedCost;
            newLabel.lmSRCsState = new List<double>(nonExtendedLabel.lmSRCsState);

            newLabel.duration += bucketArc.resourceConsumption;
            newLabel.time -= bucketArc.resourceConsumption;
            if (newLabel.time < boundOnHeadVertex.lowerBound ||  newLabel.time <= threshold)
            {
                return flag;
            }

            // Update the set of processed jobs. Job 0 and Job numOfJob+1 are the begin and end flags
            newLabel.setOfProcessedJobs = new List<int>(nonExtendedLabel.setOfProcessedJobs);
            newLabel.setOfBucketIndex = new List<int>(nonExtendedLabel.setOfBucketIndex);
            if ((newLabel.firstJob != weights.Length + 1) && (newLabel.firstJob != 0))
            {
                newLabel.setOfProcessedJobs.Add(newLabel.firstJob);
                newLabel.setOfBucketIndex.Add(bucketArc.headBucket.index);
            }

            // Update the reduced cost
            // The reduced cost on the bucket currentBucketArc of the forward nextLabel and the backward nextLabel are same
            double weight = 0;
            double dualOfJobs = 0;

            if ((newLabel.firstJob != weights.Length + 1) && (newLabel.firstJob != 0))
            {
                weight = weights[newLabel.firstJob - 1];
                dualOfJobs = dualsOfJobs[newLabel.firstJob - 1];
            }
            newLabel.cumulativeWeight += weight;
            newLabel.baseReducedCost += bucketArc.resourceConsumption * newLabel.cumulativeWeight - dualOfJobs;

            // Update the set of lmSRCs
            for (int o = 0; o < lmSRCs.Count; o++)
            {
                if (dualsOfLmSRCs[o] >= 0) continue;
                if (!lmSRCs[o].memorySet[machineID].Contains(newLabel.firstJob))
                {
                    newLabel.lmSRCsState[o] = 0;
                }
                else
                {
                    if (lmSRCs[o].subSet.Contains(newLabel.firstJob))
                    {
                        newLabel.lmSRCsState[o] += 0.5;
                        if (newLabel.lmSRCsState[o] >= 1)
                        {
                            newLabel.lmSRCsState[o] -= 1;
                            newLabel.baseReducedCost -= dualsOfLmSRCs[o];
                        }
                    }
                }
            }
            flag = true;
            return flag;
        }
        public bool BackwardExtendWithCompletionBound(BackwardLabel nonExtendedLabel, BucketArc bucketArc, BackwardLabel newLabel, int machineID, int[] weights, double[] dualsOfJobs, List<double> dualsOfLmSRCs, DynamicShrinkBound boundOnHeadVertex, double threshold, List<LmSRCOfVertex> lmSRCs, double gap, Dictionary<int, (double MinReducedCost, double MinTime)> forwardValues, string directions)
        {
            bool flag = false;
            // Update the current job
            newLabel.firstJob = bucketArc.arc[1];
            // Update the current completeTime
            // The time consumptions on the bucket currentBucketArc of  the forward nextLabel and the backward nextLabel are same
            newLabel.duration = nonExtendedLabel.duration;
            newLabel.time = nonExtendedLabel.time;
            newLabel.cumulativeWeight = nonExtendedLabel.cumulativeWeight;
            newLabel.baseReducedCost = nonExtendedLabel.baseReducedCost;
            newLabel.lmSRCsState = new List<double>(nonExtendedLabel.lmSRCsState);

            newLabel.duration += bucketArc.resourceConsumption;
            newLabel.time -= bucketArc.resourceConsumption;
            if (newLabel.time < forwardValues[newLabel.firstJob].MinTime || newLabel.time <= threshold)
            {
                return flag;
            }

            // Update the set of processed jobs. Job 0 and Job numOfJob+1 are the begin and end flags
            newLabel.setOfProcessedJobs = new List<int>(nonExtendedLabel.setOfProcessedJobs);
            newLabel.setOfBucketIndex = new List<int>(nonExtendedLabel.setOfBucketIndex);
            if ((newLabel.firstJob != weights.Length + 1) && (newLabel.firstJob != 0))
            {
                newLabel.setOfProcessedJobs.Add(newLabel.firstJob);
                newLabel.setOfBucketIndex.Add(bucketArc.headBucket.index);
            }

            // Update the reduced cost
            // The reduced cost on the bucket currentBucketArc of the forward nextLabel and the backward nextLabel are same
            double weight = 0;
            double dualOfJobs = 0;

            if ((newLabel.firstJob != weights.Length + 1) && (newLabel.firstJob != 0))
            {
                weight = weights[newLabel.firstJob - 1];
                dualOfJobs = dualsOfJobs[newLabel.firstJob - 1];
            }
            newLabel.cumulativeWeight += weight;
            newLabel.baseReducedCost += bucketArc.resourceConsumption * newLabel.cumulativeWeight - dualOfJobs;

            // Update the set of lmSRCs
            for (int o = 0; o < lmSRCs.Count; o++)
            {
                if (dualsOfLmSRCs[o] >= 0) continue;
                if (!lmSRCs[o].memorySet[machineID].Contains(newLabel.firstJob))
                {
                    newLabel.lmSRCsState[o] = 0;
                }
                else
                {
                    if (lmSRCs[o].subSet.Contains(newLabel.firstJob))
                    {
                        newLabel.lmSRCsState[o] += 0.5;
                        if (newLabel.lmSRCsState[o] >= 1)
                        {
                            newLabel.lmSRCsState[o] -= 1;
                            newLabel.baseReducedCost -= dualsOfLmSRCs[o];
                        }
                    }
                }
            }

            if (newLabel.baseReducedCost + forwardValues[newLabel.firstJob].MinReducedCost + forwardValues[newLabel.firstJob].MinTime*(newLabel.cumulativeWeight) > gap + 0.001) 
            {
                return flag;
            }
            flag = true;
            return flag;
        }
        /// <summary>
        /// Check dominated in comp wise smaller jumpBuckets
        /// </summary>
        /// <param name="label"></param>
        /// <param name="bucketContainedLabel"></param>
        /// <param name="bucket"></param>
        /// <param name="visitedBuckets"></param>
        /// <param name="bucketGraph"></param>
        /// <param name="lmSRCs"></param>
        /// <returns></returns>
        public bool DominatedInCompWiseSmallerBuckets(ForwardLabel label, Bucket bucketContainedLabel, Bucket bucket, List<Bucket> visitedBuckets, BucketGraph bucketGraph, List<double> dualsOfLmSRCs, string directions)
        {
            bool flag = false;
            visitedBuckets.Add(bucket);

            // If the reduced cost of the nextLabel is smaller than the minimal reduced cost of the tempBucket. Hence, labelSet in the tempBucket are dominated by the nextLabel without check
            if ( (label.reducedCost < bucket.minReducedCost))
            //if ((bucket.topologyOrder < bucketContainedLabel.topologyOrder) && (label.reducedCost < bucket.minReducedCost))
            {
                return flag;
            }

            // Check if the nextLabel is dominated by nextLabel in adj comp wise smaller jumpBuckets
            if (bucketContainedLabel.index != bucket.index)
            {
                // Check whether the nextLabel is dominated by the labelSet in the tempBucket
                if (DominatedByLabelsInBucket(label, bucket, dualsOfLmSRCs, directions))
                {
                    flag = true;
                    return flag;
                }
            }

            // Check if the nextLabel is dominated by nextLabel in all comp wise smaller jumpBuckets omce through recursion
            List<Bucket> adjComponentWiseSmallerBuckets = bucketGraph.adjComponentWiseSmallerBuckets[bucket].ToList();
            for (int i = 0; i < adjComponentWiseSmallerBuckets.Count; i++)
            {
                if (!visitedBuckets.Contains(adjComponentWiseSmallerBuckets[i]))
                {
                    if (DominatedInCompWiseSmallerBuckets(label, bucketContainedLabel, adjComponentWiseSmallerBuckets[i], visitedBuckets, bucketGraph, dualsOfLmSRCs, directions))
                    {
                        flag = true;
                        return flag;
                    }
                }
            }

            return flag;
        }
        public bool BackwardDominatedInCompWiseSmallerBuckets(BackwardLabel label, Bucket bucketContainedLabel, Bucket bucket, List<Bucket> visitedBuckets, BucketGraph bucketGraph, List<double> dualsOfLmSRCs, string directions)
        {
            bool flag = false;
            visitedBuckets.Add(bucket);

            // If the reduced cost of the nextLabel is smaller than the minimal reduced cost of the tempBucket. Hence, labelSet in the tempBucket are dominated by the nextLabel without check
            if ((label.baseReducedCost < bucket.minReducedCost))
            //if ((bucket.topologyOrder < bucketContainedLabel.topologyOrder) && (label.reducedCost < bucket.minReducedCost))
            {
                return flag;
            }

            // Check if the nextLabel is dominated by nextLabel in adj comp wise smaller jumpBuckets
            if (bucketContainedLabel.index != bucket.index)
            {
                // Check whether the nextLabel is dominated by the labelSet in the tempBucket
                if (BackwardDominatedByLabelsInBucket(label, bucket, dualsOfLmSRCs, directions))
                {
                    flag = true;
                    return flag;
                }
            }

            // Check if the nextLabel is dominated by nextLabel in all comp wise smaller jumpBuckets omce through recursion
            List<Bucket> adjComponentWiseSmallerBuckets = bucketGraph.adjComponentWiseSmallerBuckets[bucket].ToList();
            for (int i = 0; i < adjComponentWiseSmallerBuckets.Count; i++)
            {
                if (!visitedBuckets.Contains(adjComponentWiseSmallerBuckets[i]))
                {
                    if (BackwardDominatedInCompWiseSmallerBuckets(label, bucketContainedLabel, adjComponentWiseSmallerBuckets[i], visitedBuckets, bucketGraph, dualsOfLmSRCs, directions))
                    {
                        flag = true;
                        return flag;
                    }
                }
            }

            return flag;
        }
        /// <summary>
        /// Check dominated by the labelSet in a bucket
        /// </summary>
        /// <param name="label"></param>
        /// <param name="bucket"></param>
        /// <param name="lmSRCs"></param>
        /// <returns></returns>
        public bool DominatedByLabelsInBucket(ForwardLabel label, Bucket bucket, List<double> dualsOfLmSRCs, string directions)
        {
            bool flag = false;
            for (int i = 0; i < bucket.labelSet.Count; i++)
            {
                double sumDualsLmSRCS = 0;
                for (int o = 0; o < bucket.labelSet[i].lmSRCsState.Count; o++)
                {
                    if (bucket.labelSet[i].lmSRCsState[o] > label.lmSRCsState[o])
                    {
                        sumDualsLmSRCS += dualsOfLmSRCs[o];
                    }
                }

                if (((bucket.labelSet[i].time < label.time) && (bucket.labelSet[i].reducedCost - sumDualsLmSRCS <= label.reducedCost)) || ((bucket.labelSet[i].time <= label.time) && (bucket.labelSet[i].reducedCost - sumDualsLmSRCS < label.reducedCost)))
                {
                    flag = true;
                    break;
                }
            }

            return flag;
        }
        public bool BackwardDominatedByLabelsInBucket(BackwardLabel label, Bucket bucket, List<double> dualsOfLmSRCs, string directions)
        {
            bool flag = false;
            for (int i = 0; i < bucket.backwardLabelSet.Count; i++)
            {
                double sumDualsLmSRCS = 0;
                for (int o = 0; o < bucket.backwardLabelSet[i].lmSRCsState.Count; o++)
                {
                    if (bucket.backwardLabelSet[i].lmSRCsState[o] > label.lmSRCsState[o])
                    {
                        sumDualsLmSRCS += dualsOfLmSRCs[o];
                    }
                }

                if (((bucket.backwardLabelSet[i].time > label.time) && (bucket.backwardLabelSet[i].baseReducedCost - sumDualsLmSRCS <= label.baseReducedCost) && (bucket.backwardLabelSet[i].cumulativeWeight <= label.cumulativeWeight)) || ((bucket.backwardLabelSet[i].time >= label.time) && (bucket.backwardLabelSet[i].baseReducedCost - sumDualsLmSRCS < label.baseReducedCost) && (bucket.backwardLabelSet[i].cumulativeWeight <= label.cumulativeWeight)) || ((bucket.backwardLabelSet[i].time >= label.time) && (bucket.backwardLabelSet[i].baseReducedCost - sumDualsLmSRCS <= label.baseReducedCost) && (bucket.backwardLabelSet[i].cumulativeWeight < label.cumulativeWeight)))
                {
                    flag = true;
                    break;
                }
            }

            return flag;
        }
        /// <summary>
        /// Concatenate labelSet
        /// </summary>
        /// <param name="forwardLabel"></param>
        /// <param name="backwardBucket"></param>
        /// <param name="visitedBuckets"></param>
        /// <param name="processingTime"></param>
        /// <param name="weights"></param>
        /// <param name="dualsOfJobs"></param>
        public void ConcatenateLabels(ForwardLabel forwardLabel, Bucket backwardBucket, List<Bucket> visitedBuckets, BucketGraph backwardBucketGraph, List<ForwardLabel> nonDominatedLabelsSet, double[] processingTime, int[] weights, double[] dualsOfJobs, List<double> dualsOfLmSRCs)
        {
            visitedBuckets.Add(backwardBucket);

            double completionBound = forwardLabel.reducedCost + weights[backwardBucket.vertex - 1] * (forwardLabel.time + processingTime[backwardBucket.vertex - 1]) - dualsOfJobs[backwardBucket.vertex - 1] + backwardBucket.minReducedCost;

            if (completionBound < nonDominatedLabelsSet[0].reducedCost)
            {
                foreach (ForwardLabel backwardLabel in backwardBucket.labelSet)
                {
                    if (forwardLabel.lastJob != backwardLabel.lastJob)
                    {
                        double currentCompleteTime = forwardLabel.time + processingTime[backwardBucket.vertex - 1];
                        if (currentCompleteTime <= backwardLabel.time)
                        {
                            // If the intersection is empty
                            List<int> intersection = backwardLabel.setOfProcessedJobs.Intersect(forwardLabel.setOfProcessedJobs).ToList();
                            if (intersection.Count == 0)
                            {
                                ForwardLabel completeLabel = new ForwardLabel();
                                completeLabel._isExtended = true;
                                completeLabel.setOfProcessedJobs.AddRange(new List<int>(forwardLabel.setOfProcessedJobs));
                                for (int i = backwardLabel.setOfProcessedJobs.Count - 1; i >= 0; i--)
                                {
                                    completeLabel.setOfProcessedJobs.Add(backwardLabel.setOfProcessedJobs[i]);
                                }
                                completeLabel.lastJob = weights.Length + 1;
                                completeLabel.reducedCost = forwardLabel.reducedCost + weights[backwardBucket.vertex - 1] * currentCompleteTime - dualsOfJobs[backwardBucket.vertex - 1] + backwardLabel.reducedCost;

                                #region Exact Reduced Cost
                                //completeLabel.reducedCost = forwardLabel.reducedCost + weights[backwardBucket.vertex - 1] * backwardLabel.completeTime - dualsOfJobs[backwardBucket.vertex - 1] + backwardLabel.reducedCost;
                                //double error = 0;
                                //for (int j = 0; j < backwardLabel.setOfProcessedJobs.Count; j++)
                                //{
                                //    error += weights[backwardLabel.setOfProcessedJobs[j] - 1];
                                //}
                                //error = error * (backwardLabel.completeTime - currentCompleteTime);
                                //completeLabel.reducedCost -= error;
                                #endregion

                                
                            }
                        }
                    }
                }

                List<Bucket> adjComponentWiseSmallerBuckets = backwardBucketGraph.adjComponentWiseSmallerBuckets[backwardBucket].ToList();
                for (int i = 0; i < adjComponentWiseSmallerBuckets.Count; i++)
                {
                    if (!visitedBuckets.Contains(adjComponentWiseSmallerBuckets[i]))
                    {
                        ConcatenateLabels(forwardLabel, adjComponentWiseSmallerBuckets[i], visitedBuckets, backwardBucketGraph, nonDominatedLabelsSet, processingTime, weights, dualsOfJobs, dualsOfLmSRCs);
                    }
                }
            }
        }
        public BucketGraph GeneralForwardLabelAlgorithm(Solution solution, string directions, int machineID, double threshold, UPMSPInstances instance, Parameters parameters, Switcher switcher) {
           
            // Initialize the bucket graph
            BucketGraph bucketGraph = new BucketGraph();
            bucketGraph = solution.forwardBucketGraphs[machineID];

            bucketGraph.nonDominatedLabelsSet = new List<ForwardLabel>();
            foreach (Bucket temBucket in bucketGraph.buckets)
            {
                temBucket.labelSet = new List<ForwardLabel>();
                temBucket.minReducedCost = double.MaxValue;
            }

            // Obtain the initial nextLabel 
            ForwardLabel initialLabel = new ForwardLabel();
            initialLabel.reducedCost = -solution.dualsOfMachines[machineID];
            initialLabel.lastJob = 0;
            initialLabel.time = 0;
            initialLabel.setOfProcessedJobs = new List<int>();
            initialLabel.setOfBucketIndex = new List<int>();
            initialLabel.lmSRCsState = new List<double>();
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                initialLabel.lmSRCsState.Add(0);
            }
            initialLabel._isExtended = false;

            bucketGraph.buckets[0].labelSet.Add(initialLabel);

            while (CheckNonExtendedForwardLabels(bucketGraph)){
                foreach (Bucket tailVertex in bucketGraph.buckets) 
                {
                    foreach (ForwardLabel label in tailVertex.labelSet) 
                    {
                        if (label._isExtended) continue;
                        foreach (BucketArc arc in bucketGraph.adjListOfBucketArcs[tailVertex])
                        {
                            ForwardLabel newLabel = new ForwardLabel();
                            if (GeneralExtend(label, arc, newLabel, machineID, instance.weights, solution.dualsOfJobs, solution.dualsOfLmSRCsOfVertex, bucketGraph.dynamicShrinkBound[arc.headBucket.vertex], threshold, solution.usedLmSRCs, directions)) 
                            { 
                                Bucket headVertex = arc.headBucket;
                                if (!DominatedByLabelsInBucket(newLabel, headVertex, solution.dualsOfLmSRCsOfVertex, directions)) 
                                {
                                    label._isExtended = false;
                                    headVertex.RemoveDominatedLabelInBucket(newLabel, solution.dualsOfLmSRCsOfVertex, directions);
                                    headVertex.labelSet.Add(newLabel);
                                }
                            }
                        }
                        label._isExtended = true;
                    }
                }
            }
            bucketGraph.nonDominatedLabelsSet = new List<ForwardLabel>(bucketGraph.buckets.Last().labelSet);
            bucketGraph.nonDominatedLabelsSet = bucketGraph.nonDominatedLabelsSet.OrderBy(o => o.reducedCost).ToList();
            return bucketGraph;
        }
        public bool GeneralExtend(ForwardLabel nonExtendedLabel, BucketArc bucketArc, ForwardLabel newLabel, int machineID, int[] weights, double[] dualsOfJobs, List<double> dualsOfLmSRCs, DynamicShrinkBound boundOnHeadVertex, double threshold, List<LmSRCOfVertex> lmSRCs, string directions)
        {
            bool flag = false;
            // Update the current job
            newLabel.lastJob = bucketArc.arc[1];
            // Update the current completeTime
            // The time consumptions on the bucket currentBucketArc of  the forward nextLabel and the backward nextLabel are same
            newLabel.time = nonExtendedLabel.time;
            newLabel.reducedCost = nonExtendedLabel.reducedCost;
            newLabel.lmSRCsState = new List<double>(nonExtendedLabel.lmSRCsState);

            newLabel.time += bucketArc.resourceConsumption;
            if (newLabel.time > boundOnHeadVertex.upperBound || newLabel.time > threshold)
            {
                return flag;
            }

            // Update the set of processed jobs. Job 0 and Job numOfJob+1 are the begin and end flags
            newLabel.setOfProcessedJobs = new List<int>(nonExtendedLabel.setOfProcessedJobs);
            newLabel.setOfBucketIndex = new List<int>(nonExtendedLabel.setOfBucketIndex);
            if ((newLabel.lastJob != weights.Length + 1) && (newLabel.lastJob != 0))
            {
                newLabel.setOfProcessedJobs.Add(newLabel.lastJob);
                newLabel.setOfBucketIndex.Add(bucketArc.headBucket.index);
            }

            // Update the reduced cost
            // The reduced cost on the bucket currentBucketArc of the forward nextLabel and the backward nextLabel are same
            double weight = 0;
            double dualOfJobs = 0;
            double completeTime = 0;

            if ((newLabel.lastJob != weights.Length + 1))
            {
                weight = weights[newLabel.lastJob - 1];
                dualOfJobs = dualsOfJobs[newLabel.lastJob - 1];
                completeTime = newLabel.time;
            }
            newLabel.reducedCost += weight * completeTime - dualOfJobs;

            // Update the set of lmSRCs
            for (int o = 0; o < lmSRCs.Count; o++)
            {
                if (dualsOfLmSRCs[o] >= 0) continue;
                if (!lmSRCs[o].memorySet[machineID].Contains(newLabel.lastJob))
                {
                    newLabel.lmSRCsState[o] = 0;
                }
                else
                {
                    if (lmSRCs[o].subSet.Contains(newLabel.lastJob))
                    {
                        newLabel.lmSRCsState[o] += 0.5;
                        if (newLabel.lmSRCsState[o] >= 1)
                        {
                            newLabel.lmSRCsState[o] -= 1;
                            newLabel.reducedCost -= dualsOfLmSRCs[o];
                        }
                    }
                }
            }
            flag = true;
            return flag;
        }
        public bool CheckNonExtendedForwardLabels(BucketGraph graph) {
            foreach (Bucket vertex in graph.buckets)
            {
                if (vertex == null) continue;
                foreach (ForwardLabel label in vertex.labelSet){
                    if (!label._isExtended){
                        return true;
                    }
                }
            }
            return false;
        }
    }

    public class RowAndColumnGeneration
    {
        /// <summary>
        ///  Row-and-Column Generation Method
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="numJobs"></param>
        /// <param name="numMachines"></param>
        /// <param name="RMPSolver"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="numOfAddedCuts"></param>
        /// <returns></returns>
        public RowAndColumnGeneration(Solution solution, UPMSPInstances instance, Parameters parameters, RMPSolver RMPSolver, Switcher switcher, SolutionInformation solutionInfo)
        {
            RowGeneration cutGeneration = new RowGeneration();
            ColumnGeneration columnGeneration = new ColumnGeneration();
            Auxiliary auxiliary = new Auxiliary();

            int iter = 0;
            List<List<int>> subSets = cutGeneration.GetSubSets(instance.numJobs).Take(parameters.numOfSubsets).ToList();
            while (true)
            {
                iter++;

                DateTime startTime = DateTime.Now;

                List<LmSRCOfVertex> lmSRCs = cutGeneration.ProduceLmSRCs(instance.numMachines, subSets, solution.usedPartialScheduleSet, solution.yks);
                lmSRCs = lmSRCs.OrderByDescending(x => x.violation).ToList();
                if (lmSRCs.Count > parameters.numAddedLmSRCs)
                {
                    for (int i = parameters.numAddedLmSRCs; i < lmSRCs.Count; i++)
                    {
                        lmSRCs.RemoveAt(i);
                        i--;
                    }
                }

                DateTime endTime = DateTime.Now;
                solutionInfo.separationTime += (endTime - startTime).TotalSeconds;

                for (int c = 0; c < lmSRCs.Count; c++)
                {
                    foreach (LmSRCOfVertex lmSRCOfVertex in solution.usedLmSRCs)
                    {
                        if (auxiliary.CheckIfListsEqual(lmSRCs[c].subSet, lmSRCOfVertex.subSet))
                        {
                            for (int m = 0; m < instance.numMachines; m++)
                            {
                                lmSRCOfVertex.memorySet[m] = lmSRCOfVertex.memorySet[m].Union(lmSRCs[c].memorySet[m]).ToList();
                            }
                            lmSRCs.RemoveAt(c);
                            c--;
                            break;
                        }
                    }
                }

                if (lmSRCs.Count == 0)
                {
                    break;
                }

                RMPSolver.AddLmSRCs(instance.numMachines, lmSRCs, RMPSolver);
                solution.usedLmSRCs.AddRange(lmSRCs);

                if (switcher.bidirectionalLabeling)
                {
                    columnGeneration = new ColumnGeneration(instance, parameters, RMPSolver, solution, "Heuristic", switcher, solutionInfo);
                }
                columnGeneration = new ColumnGeneration(instance, parameters, RMPSolver, solution, "Exact", switcher, solutionInfo);

                if (iter > parameters.maxIterationsLmSRCs)
                {
                    break;
                }
            }

            solutionInfo.numOfCCGIterations += iter;
        }
    }

    public class VariableFixingByReducedCosts
    {
        public VariableFixingByReducedCosts() { }
        /// <summary>
        /// Variable Fixing by Reduced Costs Operation
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="upperBound"></param>
        /// <param name="lowerBound"></param>
        /// <param name="numMachines"></param>
        public VariableFixingByReducedCosts(Solution solution, double upperBound, UPMSPInstances instance, Parameters parameters, SolutionInformation solutionInfo, Switcher switcher)
        {
            double lowerBound = solution.dualsOfMachines.Sum() + solution.dualsOfJobs.Sum() + solution.dualsOfLmSRCsOfVertex.Sum();
            double gap = upperBound - lowerBound;
            double threshold = gap + 0.00001;

            for (int k = 0; k < instance.numMachines; k++)
            {
                PSPSolver solver = new PSPSolver();
                double forwardThreshold = solution.forwardBucketGraphs[k].dynamicShrinkBound[0].upperBound;
                BucketGraph forwardBucketGraph = solution.forwardBucketGraphs[k];
                var forwardLabelsMap = new Dictionary<int, List<ForwardLabel>>();
                foreach (var bucket in solution.forwardBucketGraphs[k].buckets)
                {
                    if (bucket.labelSet.Count == 0) continue;
                    int u = bucket.vertex;
                    if (!forwardLabelsMap.ContainsKey(u)) forwardLabelsMap[u] = new List<ForwardLabel>();
                    forwardLabelsMap[u].AddRange(bucket.labelSet);
                }
                var fwdExtremes = new Dictionary<int, (double MinRC, double MinTime)>();
                foreach (var kvp in forwardLabelsMap)
                {
                    var labels = kvp.Value;
                    labels.Sort((a, b) => a.reducedCost.CompareTo(b.reducedCost));
                    double minRc = labels[0].reducedCost; 
                    double minTime = double.MaxValue;
                    foreach (var l in labels)
                    {
                        if (l.time < minTime) minTime = l.time;
                    }
                    fwdExtremes[kvp.Key] = (minRc, minTime);
                }

                double backwardThreshold = 0;
                BucketGraph backwardBucketGraph = solution.backwardBucketGraphs[k];
                backwardBucketGraph = solver.BackwardLabelAlgorithmWithCompletionBound(solution, "Backward", k, backwardThreshold, gap, fwdExtremes, instance, parameters, switcher);

                var backwardLabelsMap = new Dictionary<int, List<BackwardLabel>>();
                var bwdExtremes = new Dictionary<int, (double MinBaseRC, double MinSlope)>();
                foreach (var bucket in solution.backwardBucketGraphs[k].buckets)
                {
                    if (bucket.backwardLabelSet.Count == 0) continue;
                    int v = bucket.vertex;
                    if (!backwardLabelsMap.ContainsKey(v)) backwardLabelsMap[v] = new List<BackwardLabel>();
                    backwardLabelsMap[v].AddRange(bucket.backwardLabelSet);
                }

                foreach (var kvp in backwardLabelsMap)
                {
                    var labels = kvp.Value;
                    labels.Sort((a, b) => a.baseReducedCost.CompareTo(b.baseReducedCost));
                    double minBaseRc = labels[0].baseReducedCost;
                    double minSlope = double.MaxValue;
                    foreach (var l in labels)
                    {
                        if (l.cumulativeWeight < minSlope) minSlope = l.cumulativeWeight;
                    }
                    bwdExtremes[kvp.Key] = (minBaseRc, minSlope);
                }

                List<int[]> nonImprovingArcs = new List<int[]>();
                for (int tail = 0; tail < forwardBucketGraph.dynamicShrinkBound.Count; tail++)
                {
                    if (!forwardLabelsMap.ContainsKey(tail)) continue;

                    List<int> succeed = forwardBucketGraph.dynamicShrinkBound[tail].succeedOrderingRestriction;
                    for (int j = 0; j < succeed.Count; j++)
                    {
                        int head = succeed[j];
                        if (!backwardLabelsMap.ContainsKey(head)) continue;

                        double optimisticTotal = fwdExtremes[tail].MinRC
                                               + bwdExtremes[head].MinBaseRC
                                               + (fwdExtremes[tail].MinTime * bwdExtremes[head].MinSlope);

                        if (optimisticTotal > threshold)
                        {
                            nonImprovingArcs.Add(new int[] { tail, head });
                            continue;
                        }

                        bool keepArc = false;
                        List<ForwardLabel> fwdList = forwardLabelsMap[tail];
                        List<BackwardLabel> bwdList = backwardLabelsMap[head];

                        foreach (ForwardLabel fwLabel in fwdList)
                        {
                            if (fwLabel.reducedCost + bwdExtremes[head].MinBaseRC + fwLabel.time * bwdExtremes[head].MinSlope > threshold)
                            {
                                break; 
                            }

                            foreach (BackwardLabel bwLabel in bwdList)
                            {
                                if (fwLabel.time + bwLabel.duration < forwardThreshold + 1e-4)
                                {
                                    double currentRc = fwLabel.reducedCost
                                                     + bwLabel.baseReducedCost
                                                     + fwLabel.time * bwLabel.cumulativeWeight;

                                    if (currentRc > threshold) continue;

                                    for (int o = 0; o < solution.dualsOfLmSRCsOfVertex.Count; o++)
                                    {
                                        if (fwLabel.lmSRCsState[o] + bwLabel.lmSRCsState[o] >= 1.0 - 1e-6)
                                        {
                                            currentRc -= solution.dualsOfLmSRCsOfVertex[o]; 
                                        }
                                    }

                                    if (currentRc <= threshold)
                                    {
                                        keepArc = true;
                                        break; 
                                    }
                                }
                            }
                            if (keepArc) break;
                        }
                        if (!keepArc)
                        {
                            nonImprovingArcs.Add(new int[] { tail, head });
                        }
                    }
                }
                solutionInfo.numOfRemovedArcs += nonImprovingArcs.Count;

                foreach (int[] arc in nonImprovingArcs)
                {
                    solution.forwardBucketGraphs[k].UpdateBucketArcsFixingIJ_0(arc[0], arc[1]);
                    solution.forwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_0(arc[0], arc[1]);

                    solution.backwardBucketGraphs[k].UpdateBucketArcsFixingIJ_0(arc[1], arc[0]);
                    solution.backwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_0(arc[0], arc[1]);
                }

                if (switcher.dynamicShrinkBound)
                {
                    solution.forwardBucketGraphs[k].UpdateDynamicShrinkBoundStatus(nonImprovingArcs);
                    solution.forwardBucketGraphs[k].CaculateDynamicShrinkBound(instance, parameters, solution, k);
                    solution.forwardBucketGraphs[k].dynamicShrinkBound = solution.forwardBucketGraphs[k].dynamicShrinkBound.OrderBy(x => x.index).ToList();
                }
            } 

            int[] numSchedules = new int[instance.numMachines];
            for (int k = 0; k < instance.numMachines; k++)
            {
                numSchedules[k] = solution.usedPartialScheduleSet[k].Count;
            }

            RMPSolver RMPSolver = new RMPSolver(instance.numMachines);
            RMPSolver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, RMPSolver, solution.usedPartialScheduleSet);
            RMPSolver.AddLmSRCs(instance.numMachines, solution.usedLmSRCs, RMPSolver);

            RMPSolver.model.Solve();

            if (RMPSolver.model.GetStatus() == Cplex.Status.Optimal)
            {
                solution._isFeasible = true;
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine("The objective objValue of RMP is： " + RMPSolver.model.ObjValue);
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine();
                solution.objValue = RMPSolver.model.ObjValue;

                solution.yks = new List<double[]>();
                for (int k = 0; k < instance.numMachines; k++)
                {
                    double[] ys = RMPSolver.model.GetValues(RMPSolver.varYks[k].ToArray());
                    solution.yks.Add(ys);
                }
                solution.dualsOfJobs = RMPSolver.model.GetDuals(RMPSolver.constaintOfJobs.ToArray());
                solution.dualsOfMachines = RMPSolver.model.GetDuals(RMPSolver.constaintOfMachines.ToArray());
                if (solution.usedLmSRCs.Count == 0)
                {
                    solution.dualsOfLmSRCsOfVertex = new List<double>();
                }
                else
                {
                    solution.dualsOfLmSRCsOfVertex = RMPSolver.model.GetDuals(RMPSolver.constaintOfLmSRC.ToArray()).ToList();
                }
            }
            else
            {
                solution._isFeasible = false;
            }
        }

        /// <summary>
        /// Obtain Improving Arcs
        /// </summary>
        /// <param name="bucketGraph"></param>
        /// <param name="improvingArcs"></param>
        /// <param name="awaitingInspectionLabels"></param>
        /// <param name="upperBound"></param>
        /// <param name="lowerBound"></param>
        public void ObtainImprovingArcs(BucketGraph bucketGraph, List<int[]> improvingArcs, List<ForwardLabel> awaitingInspectionLabels, double upperBound, double lowerBound, string type, int numJobs)
        {
            foreach (ForwardLabel label in bucketGraph.nonDominatedLabelsSet)
            {
                if (label.reducedCost <= (upperBound - lowerBound))
                {
                    for (int j = 0; j < label.setOfProcessedJobs.Count - 1; j++)
                    {
                        int[] arc = new int[2];

                        switch (type)
                        {
                            case "VariableFixing":
                                arc = new int[2] { label.setOfProcessedJobs[j], label.setOfProcessedJobs[j + 1] };
                                break;
                        }

                        if (!improvingArcs.Any(arr => arr.SequenceEqual(arc)))
                        {
                            improvingArcs.Add(arc);
                        }
                    }

                    //if (label.setOfProcessedJobs.Count > 0)
                    //{
                    //    int[] arc = new int[2];
                    //    arc[0] = label.setOfProcessedJobs.Last();
                    //    arc[1] = numJobs - 1;

                    //    if (!improvingArcs.Any(arr => arr.SequenceEqual(arc)))
                    //    {
                    //        improvingArcs.Add(arc);
                    //    }
                    //}
                }
                else
                {
                    awaitingInspectionLabels.Add(label);
                }
            }
        }
        /// <summary>
        /// Obtain Non-Improving Arcs
        /// </summary>
        /// <param name="improvingArcs"></param>
        /// <param name="awaitingInspectionLabels"></param>
        /// <returns></returns>
        public List<int[]> ObtainNonImprovingArcs(List<int[]> improvingArcs, List<ForwardLabel> awaitingInspectionLabels, string type, int numJobs)
        {
            List<int[]> nonImprovingArcs = new List<int[]>();
            foreach (ForwardLabel label in awaitingInspectionLabels)
            {
                for (int j = 0; j < label.setOfProcessedJobs.Count - 1; j++)
                {
                    int[] arc = new int[2];

                    switch (type)
                    {
                        case "VariableFixing":
                            arc = new int[2] { label.setOfProcessedJobs[j], label.setOfProcessedJobs[j + 1] };
                            break;
                    }

                    if (!improvingArcs.Any(arr => arr.SequenceEqual(arc)))
                    {
                        if (!nonImprovingArcs.Any(arr => arr.SequenceEqual(arc)))
                        {
                            nonImprovingArcs.Add(arc);
                        }
                    }
                }

                //if (label.setOfProcessedJobs.Count > 0)
                //{
                //    int[] arc = new int[2];
                //    arc[0] = label.setOfProcessedJobs.Last();
                //    arc[1] = numJobs - 1;

                //    if (!improvingArcs.Any(arr => arr.SequenceEqual(arc)))
                //    {
                //        if (!nonImprovingArcs.Any(arr => arr.SequenceEqual(arc)))
                //        {
                //            nonImprovingArcs.Add(arc);
                //        }
                //    }
                //}

            }
            return nonImprovingArcs;
        }
    }

    public class BranchAndBound
    {
        public BranchAndBound() { }
        /// <summary>
        /// Branch-and-Bound
        /// </summary>
        /// <param name="branchingVariables"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="solution"></param>
        /// <param name="bestSolution"></param>
        public BranchAndBound(UPMSPInstances instance, Parameters parameters, Switcher switcher, SolutionInformation solutionInfo, Solution solution)
        {
            BranchAndBound branchAndBound = new BranchAndBound();
            StrongBranching strongBranching = new StrongBranching();
            List<CandidateBranchVariables> historyCandidateBranchVariables = new List<CandidateBranchVariables>();

            //------ Current Optimal Solution ------
            Node optimalNode = new Node(solutionInfo.bestSolution);

            //------ Build Search Tree ------
            Stack<Node> searchTree = new Stack<Node>();

            //------ Initialize Root Node ------
            Node rootNode = new Node(solution);
            rootNode.branchVariables = new List<CandidateBranchVariables>();

            searchTree.Push(rootNode);

            int depth = 1;

            while (searchTree.Count != 0)
            {
                depth++;

                Console.WriteLine("The current depth is: " + depth);

                //------ Depth-first-search Rule ------
                Node activeNode = searchTree.Pop();

                if (activeNode._isInteger)
                {
                    if (activeNode.objValue < optimalNode.objValue)
                    {
                        optimalNode = new Node(activeNode);
                        continue;
                    }
                }
                else
                {
                    if (activeNode.objValue >= optimalNode.objValue)
                    {
                        continue;
                    }
                    else
                    {
                        double[,,] xkij = branchAndBound.ConvertYksToXkij(instance.numMachines, instance.numJobs, new Solution(activeNode));
                        double[,] qij = branchAndBound.ConvertXkijToQij(instance.numMachines, instance.numJobs, xkij);

                        // Multi-stage Strong Branching
                        List<CandidateBranchVariables> candidateXkijBranchVariables = strongBranching.ObtainXkijCandidateBranchVariables(instance.numMachines, instance.numJobs, xkij);
                        List<CandidateBranchVariables> candidateQijBranchVariables = strongBranching.ObtainQijCandidateBranchVariables(instance.numJobs, qij);
                        List<CandidateBranchVariables> candidateBranchVariables = new List<CandidateBranchVariables>();
                        List<Node> childNodes = new List<Node>();

                        // Get process memory
                        Process currentProcess = Process.GetCurrentProcess();
                        double gigabytes = (double)currentProcess.PeakWorkingSet64 / 1073741824;

                        if ((gigabytes < 2) && (switcher.strongBranching))
                        {
                            List<CandidateBranchVariables> allCandidateBranchVariables = new List<CandidateBranchVariables>();
                            allCandidateBranchVariables.AddRange(candidateXkijBranchVariables);
                            allCandidateBranchVariables.AddRange(candidateQijBranchVariables);
                            allCandidateBranchVariables = allCandidateBranchVariables.OrderBy(x => x.nonIntegerability).ToList();

                            candidateBranchVariables = strongBranching.UpdateCandidateBranchVariablesPhase_0(parameters.numCandidatesInPhase0, allCandidateBranchVariables, historyCandidateBranchVariables, xkij, qij);
                            candidateBranchVariables = strongBranching.UpdateCandidateBranchVariablesPhase_1(activeNode, instance.numMachines, instance.numJobs, candidateBranchVariables);

                            for (int i = parameters.numCandidatesInPhase1; i < candidateBranchVariables.Count; i++)
                            {
                                historyCandidateBranchVariables.Add(candidateBranchVariables[i]);
                                if (historyCandidateBranchVariables.Count > parameters.numCandidatesInPhase0)
                                {
                                    break;
                                }
                            }

                            candidateBranchVariables = candidateBranchVariables.Take(parameters.numCandidatesInPhase1).ToList();
                            candidateBranchVariables = strongBranching.UpdateCandidateBranchVariablesPhase_2(activeNode, instance, parameters, candidateBranchVariables, switcher, solutionInfo);
                            //if (candidateBranchVariables.Count == 0) 
                            {
                                activeNode.branchVariables.Add(candidateBranchVariables[0]);
                                childNodes = strongBranching.GenerateChildNode(instance, parameters, candidateBranchVariables, activeNode, optimalNode, switcher, solutionInfo);
                            }
                        }
                        else
                        {
                            switch (switcher.branchVariable)
                            {
                                case "Xkij":
                                    candidateBranchVariables = new List<CandidateBranchVariables>(candidateXkijBranchVariables);
                                    break;
                                case "Qij":
                                    candidateBranchVariables = new List<CandidateBranchVariables>(candidateQijBranchVariables);
                                    break;
                            }
                            //if (candidateBranchVariables.Count == 0)
                            {
                                candidateBranchVariables = candidateBranchVariables.OrderBy(x => x.nonIntegerability).ToList();
                                activeNode.branchVariables.Add(candidateBranchVariables[0]);

                                for (int i = 0; i < 2; i++)
                                {
                                    Node childNode = new Node();
                                    switch (switcher.branchVariable)
                                    {
                                        case "Xkij":
                                            childNode = branchAndBound.GenerateChildNodeWithXkij(i, instance, parameters, activeNode, switcher, solutionInfo);
                                            break;
                                        case "Qij":
                                            childNode = branchAndBound.GenerateChildNodeWithQij(i, instance, parameters, activeNode, switcher, solutionInfo);
                                            break;
                                    }
                                    childNodes.Add(childNode);
                                }
                                childNodes = childNodes.OrderBy(x => x.objValue).ToList();
                            }
                        }
                        List<Node> childNodesWithIntegerSolution = new List<Node>();
                        for (int i = 0; i < childNodes.Count; i++)
                        {
                            if (childNodes[i]._isFeasible)
                            {
                                if (childNodes[i]._isInteger)
                                {
                                    childNodesWithIntegerSolution.Add(childNodes[i]);
                                    childNodes.RemoveAt(i);
                                    i--;
                                }
                                else
                                {
                                    searchTree.Push(childNodes[i]);
                                }
                            }
                        }

                        foreach (Node node in childNodesWithIntegerSolution)
                        {
                            searchTree.Push(node);
                        }
                    }
                }
            }
            solutionInfo.bestSolution = new Solution(optimalNode);
            solutionInfo.numOfExploreNodes = depth;
        }
        /// <summary>
        /// Convert xkij to Qij
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="xkij"></param>
        /// <returns></returns>
        public double[,] ConvertXkijToQij(int numMachines, int numJobs, double[,,] xkij)
        {
            double[,] Qij = new double[numJobs, numJobs];
            for (int i = 0; i < numJobs; i++)
            {
                for (int j = 0; j < numJobs; j++)
                {
                    for (int k = 0; k < numMachines; k++)
                    {
                        Qij[i, j] = Qij[i, j] + xkij[k, i, j];
                    }
                }
            }
            return Qij;
        }
        /// <summary>
        /// Convert Yks to xkij
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="Yks"></param>
        /// <param name="indexFractionalSolutions"></param>
        /// <param name="usedPartialScheduleSet"></param>
        /// <returns></returns>
        public double[,,] ConvertYksToXkij(int numMachines, int numJobs, Solution solution)
        {
            double[,,] xkij = new double[numMachines, numJobs, numJobs];
            for (int k = 0; k < numMachines; k++)
            {
                for (int s = 0; s < solution.usedPartialScheduleSet[k].Count; s++)
                {
                    if (solution.yks[k][s] > 0.00001)
                    {
                        PartialSchedule partialScheduleOnSingleMachine = solution.usedPartialScheduleSet[k][s];
                        for (int i = 0; i < partialScheduleOnSingleMachine.setOfProcessedJobs.Count - 1; i++)
                        {
                            int indexI = partialScheduleOnSingleMachine.setOfProcessedJobs[i] - 1;
                            int indexJ = partialScheduleOnSingleMachine.setOfProcessedJobs[i + 1] - 1;
                            xkij[k, indexI, indexJ] = xkij[k, indexI, indexJ] + solution.yks[k][s];
                        }
                    }
                }
            }
            return xkij;
        }
        /// <summary>
        /// Generate Child Node with Fixing Qij 
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="activeNode"></param>
        /// <returns></returns>
        public Node GenerateChildNodeWithQij(int branch, UPMSPInstances instance, Parameters parameters, Node activeNode, Switcher switcher, SolutionInformation solutionInfo)
        {
            Node node = new Node(activeNode);
            //node.usedLmSRCs = new List<LmSRCOfVertex>();

            //------Jobs h and l are selected------
            int[] rule = node.branchVariables.Last().branchVariables;

            //------ Update Bucket Graph ------
            node = UpdateBucketGraphBasedOnQij(node, branch, rule, instance.numMachines, parameters.numOfBucketOnOneVertex, switcher);

            //------ Update UsedPartialScheduleSet ------
            node = UpdateUsedPartialScheduleSetBasedOnQij(node, branch, rule, instance.numMachines);

            // Solve restricted master problem with  additional constraints
            int[] numSchedules = new int[instance.numMachines];
            for (int k = 0; k < instance.numMachines; k++)
            {
                numSchedules[k] = node.usedPartialScheduleSet[k].Count;
            }
            RMPSolver solver = new RMPSolver(instance.numMachines);
            solver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, solver, node.usedPartialScheduleSet);
            solver.AddLmSRCs(instance.numMachines, node.usedLmSRCs, solver);

            ColumnGeneration columnGeneration = new ColumnGeneration();
            //if (switcher.neighborhoodSearch)
            //{
            //    columnGeneration = new ColumnGeneration(instance, parameters, solver, node, "NeighborhoodSearch", switcher, solutionInfo);
            //}
            columnGeneration = new ColumnGeneration(instance, parameters, solver, node, "Exact", switcher, solutionInfo);

            //if ((!node._isInteger))
            //{
            //    if ((solutionInfo.bestSolution.objValue - node.objValue) / solutionInfo.bestSolution.objValue < 0.01) 
            //    {
            //        if (switcher.variableFixing)
            //        {
            //            VariableFixingByReducedCosts variableFixing = new VariableFixingByReducedCosts(node, solutionInfo.bestSolution.objValue, instance, parameters, solutionInfo, switcher);
            //        }

            //        if (!node._isInteger)
            //        {
            //            numSchedules = new int[instance.numMachines];
            //            for (int k = 0; k < instance.numMachines; k++)
            //            {
            //                numSchedules[k] = node.usedPartialScheduleSet[k].Count;
            //            }
            //            solver = new RMPSolver(instance.numMachines);
            //            solver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, solver, node.usedPartialScheduleSet);
            //            solver.AddLmSRCs(instance.numMachines, node.usedLmSRCs, solver);
            //        }
            //    }
            //}

            if (!node._isInteger)
            {
                if (switcher.rowAndColumnGeneration)
                {
                    RowAndColumnGeneration rowAndColumnGeneration = new RowAndColumnGeneration(node, instance, parameters, solver, switcher, solutionInfo);
                }
            }

            node.branchVariables = new List<CandidateBranchVariables>();
            for (int n = 0; n < activeNode.branchVariables.Count; n++)
            {
                node.branchVariables.Add(new CandidateBranchVariables(activeNode.branchVariables[n]));
            }

            return node;
        }
        /// <summary>
        /// Generate Child Node with Fixing Xkij 
        /// </summary>
        /// <param name="branch"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="activeNode"></param>
        /// <returns></returns>
        public Node GenerateChildNodeWithXkij(int branch, UPMSPInstances instance, Parameters parameters, Node activeNode, Switcher switcher, SolutionInformation solutionInfo)
        {
            Node node = new Node(activeNode);
            //node.usedLmSRCs = new List<LmSRCOfVertex>();

            //------ Jobs h and l are selected -----
            int[] rule = node.branchVariables.Last().branchVariables;

            //------ Update Bucket Graph ------
            node = UpdateBucketGraphBasedOnXkij(node, branch, rule, instance.numMachines, instance.numJobs, parameters.numOfBucketOnOneVertex, switcher);

            //------ Update UsedPartialScheduleSet ------
            node = UpdateUsedPartialScheduleSetBasedOnXkij(node, branch, rule, instance.numMachines);

            RMPSolver solver = new RMPSolver(instance.numMachines);
            int[] numSchedules = new int[instance.numMachines];
            for (int k = 0; k < instance.numMachines; k++)
            {
                numSchedules[k] = node.usedPartialScheduleSet[k].Count;
            }
            solver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, solver, node.usedPartialScheduleSet);
            solver.AddLmSRCs(instance.numMachines, node.usedLmSRCs, solver);

            ColumnGeneration columnGeneration = new ColumnGeneration();
            //if (switcher.neighborhoodSearch)
            //{
            //    columnGeneration = new ColumnGeneration(instance, parameters, solver, node, "NeighborhoodSearch", switcher, solutionInfo);
            //}
            columnGeneration = new ColumnGeneration(instance, parameters, solver, node, "Exact", switcher, solutionInfo);

            //if ((!node._isInteger))
            //{
            //    if ((solutionInfo.bestSolution.objValue - node.objValue) / solutionInfo.bestSolution.objValue < 0.01) 
            //    {
            //        if (switcher.variableFixing)
            //        {
            //            VariableFixingByReducedCosts variableFixing = new VariableFixingByReducedCosts(node, solutionInfo.bestSolution.objValue, instance, parameters, solutionInfo, switcher);
            //        }

            //        if (!node._isInteger)
            //        {
            //            numSchedules = new int[instance.numMachines];
            //            for (int k = 0; k < instance.numMachines; k++)
            //            {
            //                numSchedules[k] = node.usedPartialScheduleSet[k].Count;
            //            }
            //            solver = new RMPSolver(instance.numMachines);
            //            solver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, solver, node.usedPartialScheduleSet);
            //            solver.AddLmSRCs(instance.numMachines, node.usedLmSRCs, solver);
            //        }
            //    }
            //}

            if (!node._isInteger)
            {
                if (switcher.rowAndColumnGeneration)
                {
                    RowAndColumnGeneration rowAndColumnGeneration = new RowAndColumnGeneration(node, instance, parameters, solver, switcher, solutionInfo);
                }
            }

            node.branchVariables = new List<CandidateBranchVariables>();
            for (int n = 0; n < activeNode.branchVariables.Count; n++)
            {
                node.branchVariables.Add(new CandidateBranchVariables(activeNode.branchVariables[n]));
            }

            return node;
        }
        /// <summary>
        ///  Update bucket graphs based on Xkij
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="branch"></param>
        /// <param name="rule"></param>
        /// <param name="numMachines"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <returns></returns>
        public Node UpdateBucketGraphBasedOnXkij(Node solution, int branch, int[] rule, int numMachines, int numJobs, int numOfBucketOnOneVertex, Switcher switcher)
        {
            switch (branch)
            {
                case 0:
                    solution.forwardBucketGraphs[rule[0] - 1].UpdateBucketArcsFixingIJ_0(rule[1], rule[2]);
                    solution.forwardBucketGraphs[rule[0] - 1].UpdateJobOrderingRestrictionIJ_0(rule[1], rule[2]);

                    solution.backwardBucketGraphs[rule[0] - 1].UpdateBucketArcsFixingIJ_0(rule[2], rule[1]);
                    solution.backwardBucketGraphs[rule[0] - 1].UpdateJobOrderingRestrictionIJ_0(rule[1], rule[2]);
                    break;

                case 1:
                    for (int k = 0; k < numMachines; k++)
                    {
                        if (k == rule[0] - 1)
                        {
                            solution.forwardBucketGraphs[k].UpdateBucketArcsFixingIJ_1(rule[1], rule[2], numOfBucketOnOneVertex);
                            solution.forwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_1(rule[1], rule[2]);

                            solution.backwardBucketGraphs[k].UpdateBucketArcsFixingIJ_1(rule[2], rule[1], numOfBucketOnOneVertex);
                            solution.backwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_1(rule[1], rule[2]);
                        }
                        else
                        {
                            solution.forwardBucketGraphs[k].UpdateBucketArcsFixingKIJ_1(rule[1], rule[2]);
                            solution.forwardBucketGraphs[k].UpdateJobOrderingRestrictionKIJ_1(rule[1], rule[2], numJobs);

                            solution.backwardBucketGraphs[k].UpdateBucketArcsFixingKIJ_1(rule[2], rule[1]);
                            solution.backwardBucketGraphs[k].UpdateJobOrderingRestrictionKIJ_1(rule[1], rule[2], numJobs);
                        }
                    }
                    break;
            }

            return solution;
        }
        /// <summary>
        /// Update used partial schedule set based on Xkij
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="branch"></param>
        /// <param name="rule"></param>
        /// <param name="numMachines"></param>
        /// <returns></returns>
        public Node UpdateUsedPartialScheduleSetBasedOnXkij(Node solution, int branch, int[] rule, int numMachines)
        {
            switch (branch)
            {
                case 0:
                    // Remove partial schedules that schedule job l immediately after job h on machine v
                    solution.UpdatePartialSchedulesWithIJ_0(rule[0] - 1, rule[1], rule[2]);
                    break;
                case 1:
                    for (int k = 0; k < numMachines; k++)
                    {
                        if (k != (rule[0] - 1))
                        {
                            // Remove partial schedules that contain at least one of the jobs h and l on a machine other than machine v
                            for (int s = 0; s < solution.usedPartialScheduleSet[k].Count; s++)
                            {
                                if (solution.usedPartialScheduleSet[k][s].setOfProcessedJobs.Contains(rule[1]) || solution.usedPartialScheduleSet[k][s].setOfProcessedJobs.Contains(rule[2]))
                                {
                                    solution.usedPartialScheduleSet[k].RemoveAt(s);
                                    for (int c = 0; c < solution.usedLmSRCs.Count; c++)
                                    {
                                        solution.usedLmSRCs[c].coeff[k].RemoveAt(s);
                                    }
                                    s--;
                                }
                            }
                        }
                        else
                        {
                            // Remove partial schedules that schedule job l immediately after a job other than job h or schedule a job other than h immediately before job l
                            solution.UpdatePartialSchedulesWithIJ_1(k, rule[1], rule[2]);
                        }

                    }
                    break;
            }
            return solution;
        }
        /// <summary>
        /// Update bucket graphs based on Qij
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="branch"></param>
        /// <param name="rule"></param>
        /// <param name="numMachines"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <returns></returns>
        public Node UpdateBucketGraphBasedOnQij(Node solution, int branch, int[] rule, int numMachines, int numOfBucketOnOneVertex, Switcher switcher)
        {
            switch (branch)
            {
                case 0:
                    for (int k = 0; k < numMachines; k++)
                    {
                        solution.forwardBucketGraphs[k].UpdateBucketArcsFixingIJ_0(rule[0], rule[1]);
                        solution.forwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_0(rule[0], rule[1]);

                        solution.backwardBucketGraphs[k].UpdateBucketArcsFixingIJ_0(rule[1], rule[0]);
                        solution.backwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_0(rule[0], rule[1]);

                    }
                    break;

                case 1:
                    for (int k = 0; k < numMachines; k++)
                    {
                        solution.forwardBucketGraphs[k].UpdateBucketArcsFixingIJ_1(rule[0], rule[1], numOfBucketOnOneVertex);
                        solution.forwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_1(rule[0], rule[1]);

                        solution.backwardBucketGraphs[k].UpdateBucketArcsFixingIJ_1(rule[1], rule[0], numOfBucketOnOneVertex);
                        solution.backwardBucketGraphs[k].UpdateJobOrderingRestrictionIJ_1(rule[0], rule[1]);
                    }
                    break;
            }
            return solution;
        }
        /// <summary>
        /// Update used partial schedule set based on Qij
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="branch"></param>
        /// <param name="rule"></param>
        /// <param name="numMachines"></param>
        /// <returns></returns>
        public Node UpdateUsedPartialScheduleSetBasedOnQij(Node solution, int branch, int[] rule, int numMachines)
        {
            switch (branch)
            {
                case 0:
                    for (int k = 0; k < numMachines; k++)
                    {
                        solution.UpdatePartialSchedulesWithIJ_0(k, rule[0], rule[1]);
                    }
                    break;
                case 1:
                    for (int k = 0; k < numMachines; k++)
                    {
                        solution.UpdatePartialSchedulesWithIJ_1(k, rule[0], rule[1]);
                    }
                    break;
            }
            return solution;
        }

    }

    public class RouteEnumeration
    {
        public RouteEnumeration() { }
        /// <summary>
        /// Route Enumeration
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="solver"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="weights"></param>
        /// <param name="processingTimes"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="currentUpperBound"></param>
        /// <param name="lowerBound"></param>
        /// <returns></returns>
        public RouteEnumeration(Solution solution, Switcher switcher, UPMSPInstances instance, Parameters parameters, double currentUpperBound)
        {
            ColumnGeneration columnGeneration = new ColumnGeneration();
            PSPSolver labelAlgorithm = new PSPSolver();
            DualPriceSmoothing dualPriceSmoothing = new DualPriceSmoothing();

            double lowerBound = solution.dualsOfJobs.Sum() + solution.dualsOfMachines.Sum() + solution.dualsOfLmSRCsOfVertex.Sum();
            double gap = currentUpperBound - lowerBound;

            List<List<ForwardLabel>> promisingLabels = new List<List<ForwardLabel>>();
            int[] numEnumerated = new int[instance.numMachines];
            for (int k = 0; k < instance.numMachines; k++)
            {
                double threshold = solution.forwardBucketGraphs[k].dynamicShrinkBound[0].upperBound;
                List<ForwardLabel> promisingLabelsOnMachine = new List<ForwardLabel>();
                BucketGraph bucketGraph = labelAlgorithm.ForwardLabelAlgorithm(solution, "Forward", k, threshold, instance,  parameters, switcher);
                promisingLabelsOnMachine = bucketGraph.nonDominatedLabelsSet.OrderBy(x => x.reducedCost).ToList();
                promisingLabelsOnMachine = promisingLabelsOnMachine.Where(x => x.reducedCost <=  gap + 1).ToList();

                if (promisingLabelsOnMachine.Count < parameters.maxNumEnumeratedLabels)
                {
                    numEnumerated[k] = 1;
                }
                promisingLabels.Add(promisingLabelsOnMachine);
            }

            if (numEnumerated.Sum() == instance.numMachines)
            {
                int[] numSchedules = new int[instance.numMachines];

                for (int k = 0; k < instance.numMachines; k++)
                {
                    List<PartialSchedule> partialScheduleOnSingleMachines = columnGeneration.ConvertLabelsToPartialSchedule(k, instance.numJobs, instance.processingTimes, instance.weights, promisingLabels[k]);
                    solution.usedPartialScheduleSet[k].AddRange(new List<PartialSchedule>(partialScheduleOnSingleMachines));
                    numSchedules[k] = solution.usedPartialScheduleSet[k].Count;
                }

                IPSolver mIPCplexSolver = new IPSolver(instance.numMachines);
                mIPCplexSolver.ProduceIPModel(instance.numMachines, instance.numJobs, numSchedules, mIPCplexSolver, solution.usedPartialScheduleSet);

                mIPCplexSolver.model.Solve();

                if (mIPCplexSolver.model.GetStatus() == Cplex.Status.Optimal)
                {
                    solution._isFeasible = true;
                    Console.WriteLine("----------------------------------------------------------------------------");
                    Console.WriteLine("The objective objValue is： " + mIPCplexSolver.model.ObjValue);
                    Console.WriteLine("----------------------------------------------------------------------------");
                    Console.WriteLine();
                    solution.objValue = mIPCplexSolver.model.ObjValue;

                    solution.yks = new List<double[]>();
                    for (int k = 0; k < instance.numMachines; k++)
                    {
                        double[] ys = mIPCplexSolver.model.GetValues(mIPCplexSolver.varYks[k].ToArray());
                        solution.yks.Add(ys);
                    }
                    solution.dualsOfJobs = new double[instance.numJobs];
                    solution.dualsOfMachines = new double[instance.numMachines];
                    solution.dualsOfLmSRCsOfVertex = new List<double>();
                }
                else
                {
                    solution._isFeasible = false;
                }

                solution.CheckIntegerality();
            }
        }

        /// <summary>
        ///  Forward Route Enumeration
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="weights"></param>
        /// <param name="processingTimes"></param>
        /// <param name="currentUpperBound"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="promisingLabels"></param>
        /// <param name="enumerateState"></param>
        /// <param name="maxNumEnumeratedLabels"></param>
        public bool ForwardRouteEnumeration(Solution solution, Switcher switcher, int machineID, int[] weights, double threshold, int numOfBucketOnOneVertex, double gap, List<ForwardLabel> promisingLabels, int maxNumEnumeratedLabels)
        {
            bool enumerateState = true;
            int num = 0;

            PSPSolver labelAlgorithm = new PSPSolver();
            // Initialize the bucket graph
            BucketGraph bucketGraph = new BucketGraph();
            bucketGraph = solution.forwardBucketGraphs[machineID];
            bucketGraph.nonDominatedLabelsSet = new List<ForwardLabel>();
            foreach (Bucket temBucket in bucketGraph.buckets)
            {
                temBucket.labelSet = new List<ForwardLabel>();
                temBucket.minReducedCost = double.MaxValue;
            }

            // Obtain the initial nextLabel 
            ForwardLabel initialLabel = new ForwardLabel();
            initialLabel.reducedCost = - solution.dualsOfMachines[machineID];
            initialLabel.lastJob = 0;
            initialLabel.time = 0;
            initialLabel.setOfProcessedJobs = new List<int>();
            initialLabel.setOfBucketIndex = new List<int>();
            initialLabel.lmSRCsState = new List<double>();
            for (int o = 0; o < solution.usedLmSRCs.Count; o++)
            {
                initialLabel.lmSRCsState.Add(0);
            }
            initialLabel._isExtended = false;

            // Add the initial nextLabel to the initial bucket
            bucketGraph.stronglyConnectedComponents[0].bucket.labelSet.Add(initialLabel);

            // Extend and dominate the nextLabel
            foreach (StronglyConnectedComponent stronglyConnectedComponents in bucketGraph.stronglyConnectedComponents)
            {
                Bucket bucket = stronglyConnectedComponents.bucket;

                if (!enumerateState) break;

                foreach (ForwardLabel label in bucket.labelSet)
                {
                    if (!enumerateState) break;

                    if (label._isExtended == true) continue;

                    foreach (BucketArc bucketArc in bucketGraph.adjListOfBucketArcs[bucket])
                    {
                        if (!enumerateState) break;

                        ForwardLabel newLabel = new ForwardLabel();

                        // The new labelSet may be contained in other than the head tempBucket, and flag = false.
                        if (labelAlgorithm.Extend(label, bucketArc, newLabel, machineID, weights, solution.dualsOfJobs, solution.dualsOfLmSRCsOfVertex, bucketGraph.dynamicShrinkBound[bucketArc.headBucket.vertex], threshold, solution.usedLmSRCs, "Forward"))
                        {
                            // Find the bucket that may contain the new label
                            Bucket newBucket = new Bucket();

                            if ((newLabel.time > bucketArc.headBucket.ub))
                            {
                                int index = 1 + numOfBucketOnOneVertex * (bucketArc.headBucket.vertex - 1) + bucketArc.headBucket.index;
                                for (int b = 1; b < numOfBucketOnOneVertex - bucketArc.headBucket.index; b++)
                                {
                                    if ((bucketGraph.buckets[index + b].lb < newLabel.time) && (newLabel.time <= bucketGraph.buckets[index + b].ub))
                                    {
                                        newBucket = bucketGraph.buckets[index + b];
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                newBucket = bucketArc.headBucket;
                            }

                            // Check that the new nextLabel is not dominated by the labelSet in the head (new) tempBucket
                            newLabel._isExtended = false;
                            // Add the new nextLabel into the head (new) tempBucket
                            newBucket.labelSet.Add(newLabel);
                            // Remove the dominated labelSet in the head (new) tempBucket
                            newBucket.RemoveDominatedLabelInBucket(newLabel, solution.dualsOfLmSRCsOfVertex, "Forward");

                            if (newLabel.reducedCost <= gap)
                            {
                                promisingLabels.Add(newLabel);
                            }

                            num++;

                            if (num > maxNumEnumeratedLabels)
                            {
                                enumerateState = false;
                            }
                        }
                    }

                    label._isExtended = true;
                }
            }
            promisingLabels = promisingLabels.OrderBy(o => o.reducedCost).ToList();
            return enumerateState;
        }
    }

    public class StrongBranching
    {
        /// <summary>
        /// Obtain Xkij candidate branch variables
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="activeNode"></param>
        /// <param name="xkij"></param>
        /// <returns></returns>
        public List<CandidateBranchVariables> ObtainXkijCandidateBranchVariables(int numMachines, int numJobs, double[,,] xkij)
        {
            List<CandidateBranchVariables> candidateBranchVariables = new List<CandidateBranchVariables>();
            for (int k = 0; k < numMachines; k++)
            {
                for (int i = 0; i < numJobs; i++)
                {
                    for (int j = 0; j < numJobs; j++)
                    {
                        if ((xkij[k, i, j] > 0.00001) && (xkij[k, i, j] < 0.99999))
                        {
                            CandidateBranchVariables candidateBranchVariable = new CandidateBranchVariables();
                            candidateBranchVariable.variableType = "Xkij";
                            candidateBranchVariable.branchVariables = new int[3] { k + 1, i + 1, j + 1 };
                            candidateBranchVariable.nonIntegerability = Math.Abs(xkij[k, i, j] - 0.5);
                            candidateBranchVariable.pseudoCosts = 0;
                            candidateBranchVariable.childNodes = new List<Node>();
                            candidateBranchVariables.Add(candidateBranchVariable);
                        }
                    }
                }
            }
            return candidateBranchVariables;
        }
        /// <summary>
        /// Obtain Qij candidate branch variables
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="activeNode"></param>
        /// <param name="qij"></param>
        /// <returns></returns>
        public List<CandidateBranchVariables> ObtainQijCandidateBranchVariables(int numJobs, double[,] qij)
        {
            List<CandidateBranchVariables> candidateBranchVariables = new List<CandidateBranchVariables>();
            for (int i = 0; i < numJobs; i++)
            {
                for (int j = 0; j < numJobs; j++)
                {
                    if ((qij[i, j] > 0.00001) && (qij[i, j] < 0.99999))
                    {
                        CandidateBranchVariables candidateBranchVariable = new CandidateBranchVariables();
                        candidateBranchVariable.variableType = "Qij";
                        candidateBranchVariable.branchVariables = new int[2] { i + 1, j + 1 };
                        candidateBranchVariable.nonIntegerability = Math.Abs(qij[i, j] - 0.5);
                        candidateBranchVariable.pseudoCosts = 0;
                        candidateBranchVariable.childNodes = new List<Node>();
                        candidateBranchVariables.Add(candidateBranchVariable);
                    }
                }
            }
            return candidateBranchVariables;
        }
        /// <summary>
        /// Strong Branching Phase 0
        /// </summary>
        /// <param name="numCandidateBranchVariables"></param>
        /// <param name="candidateBranchVariables"></param>
        /// <param name="historyCandidateBranchVariables"></param>
        /// <param name="xkij"></param>
        /// <param name="qij"></param>
        /// <returns></returns>
        public List<CandidateBranchVariables> UpdateCandidateBranchVariablesPhase_0(int numCandidateBranchVariables, List<CandidateBranchVariables> candidateBranchVariables, List<CandidateBranchVariables> historyCandidateBranchVariables, double[,,] xkij, double[,] qij)
        {
            List<CandidateBranchVariables> currentCandidateBranchVariables = new List<CandidateBranchVariables>();
            if (historyCandidateBranchVariables.Count == 0)
            {
                if (candidateBranchVariables.Count > numCandidateBranchVariables)
                {
                    currentCandidateBranchVariables.AddRange(candidateBranchVariables.Take(numCandidateBranchVariables).ToList());
                }
                else
                {
                    currentCandidateBranchVariables.AddRange(candidateBranchVariables);
                }
            }
            else
            {
                if (candidateBranchVariables.Count > (int)(numCandidateBranchVariables / 2))
                {
                    currentCandidateBranchVariables.AddRange(candidateBranchVariables.Take((int)(numCandidateBranchVariables / 2)).ToList());
                }
                else
                {
                    currentCandidateBranchVariables.AddRange(candidateBranchVariables);
                }

                int usedHistoryCandidateBranchVariables = 0;
                for (int h = 0; h < historyCandidateBranchVariables.Count; h++)
                {
                    if (usedHistoryCandidateBranchVariables >= (int)(numCandidateBranchVariables / 2)) break;
                    double value = 0;
                    switch (historyCandidateBranchVariables[h].variableType)
                    {
                        case "Xkij":
                            value = xkij[historyCandidateBranchVariables[h].branchVariables[0] - 1, historyCandidateBranchVariables[h].branchVariables[1] - 1, historyCandidateBranchVariables[h].branchVariables[2] - 1];
                            break;
                        case "Qij":
                            value = qij[historyCandidateBranchVariables[h].branchVariables[0] - 1, historyCandidateBranchVariables[h].branchVariables[1] - 1];
                            break;
                    }

                    if ((value > 0.00001) && (value < 0.9999999))
                    {
                        historyCandidateBranchVariables[h].childNodes = new List<Node>();
                        currentCandidateBranchVariables.Add(historyCandidateBranchVariables[h]);
                        historyCandidateBranchVariables.RemoveAt(h);
                        usedHistoryCandidateBranchVariables++;
                        h--;
                        continue;
                    }
                }
            }
            return currentCandidateBranchVariables;
        }
        /// <summary>
        /// Strong Branching Phase 1
        /// </summary>
        /// <param name="activeNode"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="candidateBranchVariables"></param>
        /// <returns></returns>
        public List<CandidateBranchVariables> UpdateCandidateBranchVariablesPhase_1(Node activeNode, int numMachines, int numJobs, List<CandidateBranchVariables> candidateBranchVariables)
        {
            BranchAndBound branchAndBound = new BranchAndBound();

            foreach (CandidateBranchVariables variable in candidateBranchVariables)
            {
                variable.childNodes = new List<Node>();
                variable.pseudoCosts = 1;
                //List<List<int[,]>> Eksij = CalculateEksij(numMachines, numJobs, activeNode);

                for (int i = 0; i < 2; i++)
                {
                    Node childNode = new Node(activeNode);
                    //childNode.usedLmSRCs = new List<LmSRCOfVertex>();

                    switch (variable.variableType)
                    {
                        case "Xkij":
                            childNode = branchAndBound.UpdateUsedPartialScheduleSetBasedOnXkij(childNode, i, variable.branchVariables, numMachines);
                            break;
                        case "Qij":
                            childNode = branchAndBound.UpdateUsedPartialScheduleSetBasedOnQij(childNode, i, variable.branchVariables, numMachines);
                            break;
                    }

                    int[] numSchedules = new int[numMachines];
                    for (int k = 0; k < numMachines; k++)
                    {
                        numSchedules[k] = childNode.usedPartialScheduleSet[k].Count;
                    }
                    RMPSolver RMPSolver = new RMPSolver(numMachines);
                    RMPSolver.ProduceModel(numMachines, numJobs, numSchedules, RMPSolver, childNode.usedPartialScheduleSet);
                    RMPSolver.AddLmSRCs(numMachines, childNode.usedLmSRCs, RMPSolver);

                    RMPSolver.model.Solve();

                    if (RMPSolver.model.GetStatus() == Cplex.Status.Optimal)
                    {
                        childNode._isFeasible = true;
                        Console.WriteLine("The objective objValue of RMP is： " + RMPSolver.model.ObjValue);
                        childNode.objValue = RMPSolver.model.ObjValue;

                        childNode.yks = new List<double[]>();
                        for (int k = 0; k < numMachines; k++)
                        {
                            double[] ys = RMPSolver.model.GetValues(RMPSolver.varYks[k].ToArray());
                            childNode.yks.Add(ys);
                        }
                        childNode.dualsOfJobs = RMPSolver.model.GetDuals(RMPSolver.constaintOfJobs.ToArray());
                        childNode.dualsOfMachines = RMPSolver.model.GetDuals(RMPSolver.constaintOfMachines.ToArray());
                        if (childNode.usedLmSRCs.Count == 0)
                        {
                            childNode.dualsOfLmSRCsOfVertex = new List<double>();
                        }
                        else
                        {
                            childNode.dualsOfLmSRCsOfVertex = RMPSolver.model.GetDuals(RMPSolver.constaintOfLmSRC.ToArray()).ToList();
                        }
                    }

                    RMPSolver.model.End();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    childNode.CheckIntegerality();
                    variable.pseudoCosts = variable.pseudoCosts * (childNode.objValue - activeNode.objValue);
                    variable.childNodes.Add(childNode);
                }
            }
            candidateBranchVariables = candidateBranchVariables.OrderByDescending(x => x.pseudoCosts).ToList();
            return candidateBranchVariables;
        }

        /// <summary>
        /// Strong Branching Phase 2 
        /// </summary>
        /// <param name="activeNode"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="candidateBranchVariables"></param>
        /// <returns></returns>
        public List<CandidateBranchVariables> UpdateCandidateBranchVariablesPhase_2(Node activeNode, UPMSPInstances instance, Parameters parameters,  List<CandidateBranchVariables> candidateBranchVariables, Switcher switcher, SolutionInformation information)
        {
            BranchAndBound branchAndBound = new BranchAndBound();

            foreach (CandidateBranchVariables candidate in candidateBranchVariables)
            {
                List<Node> childNodesClone = new List<Node>();
                candidate.pseudoCosts = 1;
                for (int i = 0; i < 2; i++)
                {
                    Node childNode = candidate.childNodes[i];

                    switch (candidate.variableType)
                    {
                        case "Qij":
                            childNode = branchAndBound.UpdateBucketGraphBasedOnQij(childNode, i, candidate.branchVariables, instance.numMachines, parameters.numOfBucketOnOneVertex, switcher);
                            break;
                        case "Xkij":
                            childNode = branchAndBound.UpdateBucketGraphBasedOnXkij(childNode, i, candidate.branchVariables, instance.numMachines, instance.numJobs, parameters.numOfBucketOnOneVertex, switcher);
                            break;
                    }

                    Solution childSolution = new Solution(childNode);
                    //childSolution.usedLmSRCs = new List<LmSRCOfVertex>();
                    int[] numSchedules = new int[instance.numMachines];
                    for (int k = 0; k < instance.numMachines; k++)
                    {
                        numSchedules[k] = childSolution.usedPartialScheduleSet[k].Count;
                    }
                    RMPSolver RMPSolver = new RMPSolver(instance.numMachines);
                    RMPSolver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, RMPSolver, childSolution.usedPartialScheduleSet);
                    RMPSolver.AddLmSRCs(instance.numMachines, childSolution.usedLmSRCs, RMPSolver);

                    //RMPSolver.model.Solve();

                    //if (RMPSolver.model.GetStatus() == Cplex.Status.Optimal)
                    //{
                    //    childSolution._isFeasible = true;
                    //    Console.WriteLine("The objective objValue of RMP is： " + RMPSolver.model.ObjValue);
                    //    childSolution.objValue = RMPSolver.model.ObjValue;

                    //    childSolution.yks = new List<double[]>();
                    //    for (int k = 0; k < instance.numMachines; k++)
                    //    {
                    //        double[] ys = RMPSolver.model.GetValues(RMPSolver.varYks[k].ToArray());
                    //        childSolution.yks.Add(ys);
                    //    }
                    //    childSolution.dualsOfJobs = RMPSolver.model.GetDuals(RMPSolver.constaintOfJobs.ToArray());
                    //    childSolution.dualsOfMachines = RMPSolver.model.GetDuals(RMPSolver.constaintOfMachines.ToArray());
                    //    if (childSolution.usedLmSRCs.Count == 0)
                    //    {
                    //        childSolution.dualsOfLmSRCsOfVertex = new List<double>();
                    //    }
                    //    else
                    //    {
                    //        childSolution.dualsOfLmSRCsOfVertex = RMPSolver.model.GetDuals(RMPSolver.constaintOfLmSRC.ToArray()).ToList();
                    //    }
                    //}

                    ColumnGeneration columnGeneration = new ColumnGeneration();
                    columnGeneration = new ColumnGeneration(instance, parameters, RMPSolver, childSolution, "NeighborhoodSearch", switcher, information);
                    childNode = new Node(childSolution);
                    RMPSolver.model.EndModel();

                    childNode.branchVariables = new List<CandidateBranchVariables>();
                    for (int n = 0; n < activeNode.branchVariables.Count; n++)
                    {
                        childNode.branchVariables.Add(new CandidateBranchVariables(activeNode.branchVariables[n]));
                    }

                    candidate.pseudoCosts = candidate.pseudoCosts * (childNode.objValue - activeNode.objValue);
                    childNodesClone.Add(childNode);
                }

                candidate.childNodes = new List<Node>(childNodesClone);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            candidateBranchVariables = candidateBranchVariables.OrderByDescending(x => x.pseudoCosts).ToList();
            return candidateBranchVariables;
        }

        /// <summary>
        ///  Generate child nodes 
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="processingTimes"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="candidateBranchVariables"></param>
        /// <param name="activeNode"></param>
        /// <returns></returns>
        public List<Node> GenerateChildNode(UPMSPInstances instance, Parameters parameters, List<CandidateBranchVariables> candidateBranchVariables, Node activeNode, Node optimalNode, Switcher switcher, SolutionInformation solutionInfo)
        {
            List<Node> childNodes = new List<Node>();

            for (int i = 0; i < 2; i++)
            {
                Solution childSolution = new Solution(candidateBranchVariables[0].childNodes[i]);

                RMPSolver solver = new RMPSolver(instance.numMachines);
                int[] numSchedules = new int[instance.numMachines];
                for (int k = 0; k < instance.numMachines; k++)
                {
                    numSchedules[k] = childSolution.usedPartialScheduleSet[k].Count;
                }
                solver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, solver, childSolution.usedPartialScheduleSet);
                solver.AddLmSRCs(instance.numMachines, childSolution.usedLmSRCs, solver);

                ColumnGeneration columnGeneration = new ColumnGeneration();
                //if (switcher.neighborhoodSearch)
                //{
                //    columnGeneration = new ColumnGeneration(instance, parameters, solver, childSolution, "NeighborhoodSearch", switcher, solutionInfo);
                //}
                columnGeneration = new ColumnGeneration(instance, parameters, solver, childSolution, "Exact", switcher, solutionInfo);

                //if ((!childSolution._isInteger))
                //{
                //    if ((solutionInfo.bestSolution.objValue - childSolution.objValue) / solutionInfo.bestSolution.objValue < 0.1)
                //    {
                //        if (switcher.variableFixing)
                //        {
                //            VariableFixingByReducedCosts variableFixing = new VariableFixingByReducedCosts(childSolution, optimalNode.objValue, instance, parameters, solutionInfo, switcher);
                //        }

                //        if (!childSolution._isInteger)
                //        {
                //            numSchedules = new int[instance.numMachines];
                //            for (int k = 0; k < instance.numMachines; k++)
                //            {
                //                numSchedules[k] = childSolution.usedPartialScheduleSet[k].Count;
                //            }
                //            solver = new RMPSolver(instance.numMachines);
                //            solver.ProduceModel(instance.numMachines, instance.numJobs, numSchedules, solver, childSolution.usedPartialScheduleSet);
                //            solver.AddLmSRCs(instance.numMachines, childSolution.usedLmSRCs, solver);
                //        }
                //    }
                //}

                if (!childSolution._isInteger)
                {
                    if (switcher.rowAndColumnGeneration)
                    {
                        RowAndColumnGeneration rowAndColumnGeneration = new RowAndColumnGeneration(childSolution, instance, parameters, solver, switcher, solutionInfo);
                    }
                }

                Node node = new Node(childSolution);
                node.branchVariables = new List<CandidateBranchVariables>();
                for (int n = 0; n < activeNode.branchVariables.Count; n++)
                {
                    node.branchVariables.Add(new CandidateBranchVariables(activeNode.branchVariables[n]));
                }
                childNodes.Add(node);
            }

            //------ Bset-lower-bound Rule ------ 
            childNodes = childNodes.OrderBy(x => x.objValue).ToList();

            return childNodes;
        }

        /// <summary>
        ///Calculate Eksij
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="activeNode"></param>
        /// <returns></returns>
        public List<List<int[,]>> CalculateEksij(int numMachines, int numJobs, Node activeNode)
        {
            List<List<int[,]>> Eksij = new List<List<int[,]>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<int[,]> Esij = new List<int[,]>();
                foreach (PartialSchedule partialSchedule in activeNode.usedPartialScheduleSet[k])
                {
                    int[,] Eij = new int[numJobs, numJobs];
                    for (int i = 0; i < partialSchedule.setOfProcessedJobs.Count - 1; i++)
                    {
                        Eij[partialSchedule.setOfProcessedJobs[i] - 1, partialSchedule.setOfProcessedJobs[i + 1] - 1] = 1;
                    }
                    Esij.Add(Eij);
                }
                Eksij.Add(Esij);
            }
            return Eksij;
        }
    }

    public class HeuristicPricing
    {
        /// <summary>
        /// Neighborhood Search
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="numJobs"></param>
        /// <param name="machineID"></param>
        /// <param name="maxIterationsNeighborhoodSearch"></param>
        /// <param name="numNeighbors"></param>
        /// <param name="sizeMemorySet"></param>
        /// <param name="weights"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="random"></param>
        /// <returns></returns>
        public List<ForwardLabel> NeighborhoodSearch(Solution solution, UPMSPInstances instance,  Parameters parameters, int machineID, double threshold, Random random)
        {
            List<ForwardLabel> newLabelsOnSingleMachine = new List<ForwardLabel>();
            ForwardLabel label = InitializeHeuristicPricingLabel(solution, machineID, instance.weights, threshold, parameters.numOfBucketOnOneVertex, parameters.sizeMemorySet, random);

            for (int iter = 0; iter < parameters.maxIterationsNeighborhoodSearch; iter++)
            {
                ForwardLabel nextLabel = new ForwardLabel(label);
                for (int num = 0; num < parameters.numNeighbors; num++)
                {
                    ForwardLabel newLabel = ProduceNeighborhoodLabels(solution, newLabelsOnSingleMachine, label, machineID, instance.weights, threshold, parameters.numOfBucketOnOneVertex, parameters.sizeMemorySet, random);
                    if ((newLabel.reducedCost < nextLabel.reducedCost) && (newLabel.setOfProcessedJobs.Count > 0))
                    {
                        nextLabel = new ForwardLabel(newLabel);
                    }
                }
                label = new ForwardLabel(nextLabel);
            }
            newLabelsOnSingleMachine = newLabelsOnSingleMachine.OrderBy(x => x.reducedCost).ToList();
            return newLabelsOnSingleMachine;
        }
        /// <summary>
        /// Initialize heuristic pricing label
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="numJobs"></param>
        /// <param name="weights"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="random"></param>
        /// <returns></returns>
        public ForwardLabel InitializeHeuristicPricingLabel(Solution solution, int machineID, int[] weights, double threshold, int numOfBucketOnOneVertex, int sizeMemorySet, Random random)
        {
            BucketGraph bucketGraph = solution.forwardBucketGraphs[machineID];

            ForwardLabel previousLabel = new ForwardLabel();
            while (true)
            {
                previousLabel.reducedCost = -solution.dualsOfMachines[machineID];
                previousLabel.lastJob = 0;
                previousLabel.time = 0;
                previousLabel.setOfProcessedJobs = new List<int>();
                previousLabel.setOfBucketIndex = new List<int>();
                previousLabel.lmSRCsState = new List<double>();
                for (int o = 0; o < solution.usedLmSRCs.Count; o++)
                {
                    previousLabel.lmSRCsState.Add(0);
                }
                previousLabel._isExtended = false;

                Bucket bucket = bucketGraph.buckets[0];

                bool flag = false;

                while (previousLabel.lastJob != (weights.Length + 1))
                {
                    if (bucketGraph.adjListOfBucketArcs[bucket].Count == 0)
                    {
                        break;
                    }
                    int nextIndex = random.Next(0, bucketGraph.adjListOfBucketArcs[bucket].Count);
                    BucketArc bucketArc = bucketGraph.adjListOfBucketArcs[bucket][nextIndex];
                    ForwardLabel newLabel = new ForwardLabel();
                    bucket = UpdateCurrentBucket(solution, bucketGraph, bucket, new List<ForwardLabel>(), previousLabel, bucketArc, newLabel, machineID, weights, threshold, numOfBucketOnOneVertex, sizeMemorySet);
                    previousLabel = new ForwardLabel(newLabel);
                }
                if (previousLabel.setOfProcessedJobs.Count > 0)
                {
                    flag = true;
                }
                if (flag)
                {
                    break;
                }
            }

            return previousLabel;
        }
        /// <summary>
        ///  Produce Neighborhood Labels
        /// </summary>
        /// <param name="result"></param>
        /// <param name="labels"></param>
        /// <param name="label"></param>
        /// <param name="machineID"></param>
        /// <param name="weights"></param>
        /// <param name="threshold"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="random"></param>
        /// <returns></returns>
        public ForwardLabel ProduceNeighborhoodLabels(Solution result, List<ForwardLabel> labels, ForwardLabel label, int machineID, int[] weights, double threshold, int numOfBucketOnOneVertex, int sizeMemorySet, Random random)
        {
            BucketGraph bucketGraph = result.forwardBucketGraphs[machineID];

            ForwardLabel newLabel = new ForwardLabel();
            newLabel.reducedCost = -result.dualsOfMachines[machineID];
            newLabel.lastJob = 0;
            newLabel.time = 0;
            newLabel.setOfProcessedJobs = new List<int>();
            newLabel.setOfBucketIndex = new List<int>();
            newLabel.lmSRCsState = new List<double>();
            for (int o = 0; o < result.usedLmSRCs.Count; o++)
            {
                newLabel.lmSRCsState.Add(0);
            }
            newLabel._isExtended = true;

            Bucket bucket = bucketGraph.buckets[0];

            // Phase 0: Randomly select the index and vertex of the mutation
            int mutationIndex = random.Next(0, label.setOfProcessedJobs.Count);
            int mutationVertex = label.setOfProcessedJobs[mutationIndex];
            int mutationBucketIndex = label.setOfBucketIndex[mutationIndex];

            // Phase 1: Extend the label before the mutation index
            for (int i = 0; i < mutationIndex; i++)
            {
                BucketArc bucketArc = new BucketArc();
                for (int a = 0; a < bucketGraph.adjListOfBucketArcs[bucket].Count; a++)
                {
                    if (bucketGraph.adjListOfBucketArcs[bucket][a].headBucket.vertex == label.setOfProcessedJobs[i])
                    {
                        bucketArc = bucketGraph.adjListOfBucketArcs[bucket][a];
                        break;
                    }
                }

                ForwardLabel nextLabel = new ForwardLabel();
                bucket = UpdateCurrentBucket(result, bucketGraph, bucket, labels, newLabel, bucketArc, nextLabel, machineID, weights, threshold, numOfBucketOnOneVertex, sizeMemorySet);
                newLabel = new ForwardLabel(nextLabel);
            }

            // Phase 2: Extend the label at the mutation index along a different bucket arc from the previous one
            if (bucketGraph.adjListOfBucketArcs[bucket].Count > 1)
            {
                int nextIndex = random.Next(0, bucketGraph.adjListOfBucketArcs[bucket].Count - 1);

                BucketArc bucketArc = new BucketArc();

                int iter = 0;
                foreach (BucketArc currentBucketArc in bucketGraph.adjListOfBucketArcs[bucket])
                {
                    if (!((currentBucketArc.headBucket.vertex == mutationVertex) && (currentBucketArc.headBucket.index == mutationBucketIndex)))
                    {
                        iter++;
                    }

                    if ((iter - 1) == nextIndex)
                    {
                        bucketArc = currentBucketArc;
                        break;
                    }
                }

                ForwardLabel nextLabel = new ForwardLabel();
                bucket = UpdateCurrentBucket(result, bucketGraph, bucket, labels, newLabel, bucketArc, nextLabel, machineID, weights, threshold, numOfBucketOnOneVertex, sizeMemorySet);
                newLabel = new ForwardLabel(nextLabel);
            }

            // Phase 3: Extend the label after the mutation index
            int num = 0;
            while ((newLabel.lastJob != (weights.Length + 1)) && (bucketGraph.adjListOfBucketArcs[bucket].Count > 0))
            {
                num++;
                if (num > 10)
                {
                    break;
                }
                int nextIndex = random.Next(0, bucketGraph.adjListOfBucketArcs[bucket].Count);

                BucketArc bucketArc = bucketGraph.adjListOfBucketArcs[bucket][nextIndex];

                ForwardLabel nextLabel = new ForwardLabel();
                bucket = UpdateCurrentBucket(result, bucketGraph, bucket, labels, newLabel, bucketArc, nextLabel, machineID, weights, threshold, numOfBucketOnOneVertex, sizeMemorySet);
                newLabel = new ForwardLabel(nextLabel);
            }

            return newLabel;
        }
        /// <summary>
        /// Update current bucket
        /// </summary>
        /// <param name="result"></param>
        /// <param name="bucketGraph"></param>
        /// <param name="bucket"></param>
        /// <param name="labels"></param>
        /// <param name="nonExtendedLabel"></param>
        /// <param name="bucketArc"></param>
        /// <param name="nextLabel"></param>
        /// <param name="k"></param>
        /// <param name="weights"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <returns></returns>
        public Bucket UpdateCurrentBucket(Solution result, BucketGraph bucketGraph, Bucket bucket, List<ForwardLabel> labels, ForwardLabel nonExtendedLabel, BucketArc bucketArc, ForwardLabel nextLabel, int k, int[] weights, double threshold, int numOfBucketOnOneVertex, int sizeMemorySet)
        {
            PSPSolver labelAlgorithm = new PSPSolver();
            if (labelAlgorithm.Extend(nonExtendedLabel, bucketArc, nextLabel, k, weights, result.dualsOfJobs, result.dualsOfLmSRCsOfVertex, bucketGraph.dynamicShrinkBound[bucketArc.headBucket.vertex], threshold, result.usedLmSRCs, "Forward"))
            {
                if ((nextLabel.time > bucketArc.headBucket.ub))
                {
                    int index = 1 + numOfBucketOnOneVertex * (bucketArc.headBucket.vertex - 1) + bucketArc.headBucket.index;
                    for (int b = 1; b < numOfBucketOnOneVertex - bucketArc.headBucket.index; b++)
                    {
                        if ((bucketGraph.buckets[index + b].lb < nextLabel.time) && (nextLabel.time <= bucketGraph.buckets[index + b].ub))
                        {
                            bucket = bucketGraph.buckets[index + b];
                            break;
                        }
                    }
                }
                else
                {
                    bucket = bucketArc.headBucket;
                }
            }
            else 
            {
                nextLabel = new ForwardLabel(nonExtendedLabel);
            }

            if (nextLabel.reducedCost < -0.0001)
            {
                if (nextLabel.lastJob != (weights.Length + 1))
                {
                    if (!CheckWhetherLabelExists(nextLabel, labels))
                    {
                        if (labels.Count < sizeMemorySet)
                        {
                            labels.Add(nextLabel);
                        }
                        else
                        {
                            labels = labels.OrderBy(o => o.reducedCost).ToList();
                            if (nextLabel.reducedCost < labels.Last().reducedCost)
                            {
                                labels.Add(nextLabel);
                                labels.RemoveRange(sizeMemorySet, labels.Count - sizeMemorySet);
                            }
                        }
                    }
                }
            }

            return bucket;
        }
        /// <summary>
        /// Check whether label exists
        /// </summary>
        /// <param name="label"></param>
        /// <param name="labelSet"></param>
        /// <returns></returns>
        public bool CheckWhetherLabelExists(ForwardLabel label, List<ForwardLabel> labelSet)
        {
            bool labelExists = false;
            foreach (ForwardLabel currentLabel in labelSet)
            {
                if ((label.time == currentLabel.time) && (label.reducedCost == currentLabel.reducedCost))
                {
                    labelExists = true;
                    break;
                }
            }
            return labelExists;
        }
    }

    public class RMPSolver
    {
        public Cplex model { get; set; }
        public IObjective objective { get; set; }
        public List<List<INumVar>> varYks { get; set; }
        public List<IRange> constaintOfJobs { get; set; }
        public List<IRange> constaintOfMachines { get; set; }
        public List<IRange> constaintOfLmSRC { get; set; }
        public List<IRange> branchConstaint { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="numMachines"></param>
        public RMPSolver(int numMachines)
        {
            model = new Cplex();
            objective = model.AddMinimize();
            constaintOfJobs = new List<IRange>();
            constaintOfMachines = new List<IRange>();
            constaintOfLmSRC = new List<IRange>();
            branchConstaint = new List<IRange>();
            varYks = new List<List<INumVar>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<INumVar> YksOnSingleMachine = new List<INumVar>();
                varYks.Add(YksOnSingleMachine);
            }
        }
        /// <summary>
        /// Produce model
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="numSchedules"></param>
        /// <param name="UPMSP"></param>
        /// <param name="usedPartialScheduleSet"></param>
        public void ProduceModel(int numMachines, int numJobs, int[] numSchedules, RMPSolver UPMSP, List<List<PartialSchedule>> usedPartialScheduleSet)
        {
            model.SetParam(Cplex.Param.Simplex.Tolerances.Optimality, 1e-6);
            model.SetParam(Cplex.Param.Emphasis.Numerical, true);
            model.SetParam(Cplex.Param.RootAlgorithm, Cplex.Algorithm.Dual);
            model.SetOut(null);

            for (int k = 0; k < numMachines; k++)
            {
                for (int s = 0; s < numSchedules[k]; s++)
                {
                    UPMSP.varYks[k].Add(UPMSP.model.NumVar(0.0, 1.0));
                }
            }

            ILinearNumExpr objFunction = UPMSP.model.LinearNumExpr();

            for (int k = 0; k < numMachines; k++)
            {
                for (int s = 0; s < numSchedules[k]; s++)
                {
                    objFunction.AddTerm(usedPartialScheduleSet[k][s].TWCT, varYks[k][s]);
                }
            }
            UPMSP.objective.Expr = objFunction;

            UPMSP.constaintOfJobs = new List<IRange>();
            for (int j = 0; j < numJobs; j++)
            {
                ILinearNumExpr constaintFunction = model.LinearNumExpr();
                for (int k = 0; k < numMachines; k++)
                {
                    for (int s = 0; s < numSchedules[k]; s++)
                    {
                        constaintFunction.AddTerm(usedPartialScheduleSet[k][s].vectorOfProcessedJob[j], varYks[k][s]);
                    }
                }
                UPMSP.constaintOfJobs.Add(UPMSP.model.AddGe(constaintFunction, 1));
            }

            UPMSP.constaintOfMachines = new List<IRange>();
            for (int k = 0; k < numMachines; k++)
            {
                ILinearNumExpr constaintFunction1 = model.LinearNumExpr();
                for (int s = 0; s < numSchedules[k]; s++)
                {
                    constaintFunction1.AddTerm(1, varYks[k][s]);
                }
                UPMSP.constaintOfMachines.Add(UPMSP.model.AddLe(constaintFunction1, 1));
            }
            UPMSP.model.SetParam(Cplex.Param.RootAlgorithm, Cplex.Algorithm.Primal);
            //UPMSP.model.ExportModel("fileName.lp");
        }
        /// <summary>
        /// My solve
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="UPMSP"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        public void Solver(Solution solution, int numMachines, int numJobs)
        {
            model.Solve();
            if (model.GetStatus() == Cplex.Status.Optimal)
            {
                solution._isFeasible = true;
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine("The objective objValue of RMP is： " + model.ObjValue);
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine();
                solution.objValue = model.ObjValue;

                solution.yks = new List<double[]>();
                for (int k = 0; k < numMachines; k++)
                {
                    double[] ys = model.GetValues(varYks[k].ToArray());
                    solution.yks.Add(ys);
                }
                solution.dualsOfJobs = model.GetDuals(constaintOfJobs.ToArray());
                solution.dualsOfMachines = model.GetDuals(constaintOfMachines.ToArray());
                if (solution.usedLmSRCs.Count == 0)
                {
                    solution.dualsOfLmSRCsOfVertex = new List<double>();
                }
                else
                {
                    solution.dualsOfLmSRCsOfVertex = model.GetDuals(constaintOfLmSRC.ToArray()).ToList();
                }
                solution.CheckIntegerality();
            }
            else
            {
                solution._isFeasible = false;
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine("The problem is infeasible");
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine();
            }
        }
        /// <summary>
        /// Add New Columns
        /// </summary>
        /// <param name="numConstraints"></param>
        /// <param name="UPMSP"></param>
        /// <param name="newPartialScheduleOnSingleMachines"></param>
        public void AddColumn(int machineID, int numJobs, RMPSolver UPMSP, PartialSchedule newPartialScheduleOnSingleMachines, List<LmSRCOfVertex> lmSRCs)
        {
            Column column = UPMSP.model.Column(UPMSP.objective, newPartialScheduleOnSingleMachines.TWCT);
            RowGeneration lmSRCsGeneration = new RowGeneration();

            for (int j = 0; j < numJobs; j++)
            {
                column = column.And(UPMSP.model.Column(UPMSP.constaintOfJobs[j], newPartialScheduleOnSingleMachines.vectorOfProcessedJob[j]));
            }
            column = column.And(UPMSP.model.Column(UPMSP.constaintOfMachines[machineID], 1));

            for (int o = 0; o < UPMSP.constaintOfLmSRC.Count; o++)
            {
                double coeff = 0;
                List<int> intersection = lmSRCs[o].subSet.Intersect(newPartialScheduleOnSingleMachines.setOfProcessedJobs).ToList();
                if (intersection.Count > 1)
                {
                    coeff = lmSRCsGeneration.CalculateCoefficientOfLmSRC(newPartialScheduleOnSingleMachines, lmSRCs[o].memorySet[machineID], lmSRCs[o].subSet, 0.5);
                    column = column.And(UPMSP.model.Column(UPMSP.constaintOfLmSRC[o], coeff));
                }
                lmSRCs[o].coeff[machineID].Add(coeff);
            }

            UPMSP.varYks[machineID].Add(UPMSP.model.NumVar(column, 0.0, 1.0));
        }
        /// <summary>
        ///  Add lm-SRCs
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="lmSRCs"></param>
        /// <param name="UPMSP"></param>
        /// <param name="usedPartialScheduleSet"></param>
        public void AddLmSRCs(int numMachines, List<LmSRCOfVertex> lmSRCs, RMPSolver UPMSP)
        {
            //UPMSP.constaintOfLmSRC = new List<IRange>();
            for (int o = 0; o < lmSRCs.Count; o++)
            {
                ILinearNumExpr constaintFunction = model.LinearNumExpr();
                for (int k = 0; k < numMachines; k++)
                {
                    for (int s = 0; s < lmSRCs[o].coeff[k].Count; s++)
                    {
                        constaintFunction.AddTerm(lmSRCs[o].coeff[k][s], varYks[k][s]);
                    }
                }
                UPMSP.constaintOfLmSRC.Add(UPMSP.model.AddLe(constaintFunction, 1));
            }
        }
        /// <summary>
        /// Add Xkij branch constaints
        /// </summary>
        /// <param name="branchType"></param>
        /// <param name="Eksij"></param>
        /// <param name="UPMSP"></param>
        /// <param name="branchVariables"></param>
        /// <param name="numSchedules"></param>
        public void AddXkijBranchConstaint(int branchType, List<List<int[,]>> Eksij, RMPSolver UPMSP, int[] branchVariables, int[] numSchedules)
        {
            UPMSP.branchConstaint = new List<IRange>();
            ILinearNumExpr constaintFunction = model.LinearNumExpr();
            for (int s = 0; s < numSchedules[branchVariables[0] - 1]; s++)
            {
                constaintFunction.AddTerm(Eksij[branchVariables[0] - 1][s][branchVariables[1] - 1, branchVariables[2] - 1], varYks[branchVariables[0] - 1][s]);
            }
            UPMSP.branchConstaint.Add(UPMSP.model.AddEq(constaintFunction, branchType));
        }

        /// <summary>
        /// Add Qij branch constaints
        /// </summary>
        /// <param name="branchType"></param>
        /// <param name="numMachines"></param>
        /// <param name="Eksij"></param>
        /// <param name="UPMSP"></param>
        /// <param name="branchVariables"></param>
        /// <param name="numSchedules"></param>
        public void AddQijBranchConstaint(int branchType, int numMachines, List<List<int[,]>> Eksij, RMPSolver UPMSP, int[] branchVariables, int[] numSchedules)
        {
            UPMSP.branchConstaint = new List<IRange>();
            ILinearNumExpr constaintFunction = model.LinearNumExpr();
            for (int k = 0; k < numMachines; k++)
            {
                for (int s = 0; s < numSchedules[k]; s++)
                {
                    constaintFunction.AddTerm(Eksij[k][s][branchVariables[0] - 1, branchVariables[1] - 1], varYks[k][s]);
                }
            }
            UPMSP.branchConstaint.Add(UPMSP.model.AddEq(constaintFunction, branchType));
        }
    }
    public class IPSolver
    {
        public Cplex model { get; set; }
        public IObjective objective { get; set; }
        public List<List<INumVar>> varYks { get; set; }
        public List<IRange> constaintOfJobs { get; set; }
        public List<IRange> constaintOfMachines { get; set; }
        public List<IRange> constaintOfLmSRC { get; set; }

        /// <summary>
        ///Constructor
        /// </summary>
        /// <param name="numMachines"></param>
        public IPSolver(int numMachines)
        {
            model = new Cplex();
            objective = model.AddMinimize();
            constaintOfJobs = new List<IRange>();
            constaintOfMachines = new List<IRange>();
            constaintOfLmSRC = new List<IRange>();
            varYks = new List<List<INumVar>>();
            for (int k = 0; k < numMachines; k++)
            {
                List<INumVar> YksOnSingleMachine = new List<INumVar>();
                varYks.Add(YksOnSingleMachine);
            }
        }
        /// <summary>
        /// Produce model
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="numSchedules"></param>
        /// <param name="UPMSP"></param>
        /// <param name="usedPartialScheduleSet"></param>
        public void ProduceIPModel(int numMachines, int numJobs, int[] numSchedules, IPSolver UPMSP, List<List<PartialSchedule>> usedPartialScheduleSet)
        {
            for (int k = 0; k < numMachines; k++)
            {
                for (int s = 0; s < numSchedules[k]; s++)
                {

                    UPMSP.varYks[k].Add(UPMSP.model.BoolVar());
                }
            }

            ILinearNumExpr objFunction = UPMSP.model.LinearNumExpr();

            for (int k = 0; k < numMachines; k++)
            {
                for (int s = 0; s < numSchedules[k]; s++)
                {
                    objFunction.AddTerm(usedPartialScheduleSet[k][s].TWCT, varYks[k][s]);
                }
            }
            UPMSP.objective.Expr = objFunction;

            UPMSP.constaintOfJobs = new List<IRange>();
            for (int j = 0; j < numJobs; j++)
            {
                ILinearNumExpr constaintFunction = model.LinearNumExpr();
                for (int k = 0; k < numMachines; k++)
                {
                    for (int s = 0; s < numSchedules[k]; s++)
                    {
                        constaintFunction.AddTerm(usedPartialScheduleSet[k][s].vectorOfProcessedJob[j], varYks[k][s]);
                    }
                }
                UPMSP.constaintOfJobs.Add(UPMSP.model.AddEq(constaintFunction, 1));
            }

            UPMSP.constaintOfMachines = new List<IRange>();
            for (int k = 0; k < numMachines; k++)
            {
                ILinearNumExpr constaintFunction1 = model.LinearNumExpr();
                for (int s = 0; s < numSchedules[k]; s++)
                {
                    constaintFunction1.AddTerm(1, varYks[k][s]);
                }
                UPMSP.constaintOfMachines.Add(UPMSP.model.AddLe(constaintFunction1, 1));
            }
            UPMSP.model.SetParam(Cplex.Param.RootAlgorithm, Cplex.Algorithm.Primal);
            //UPMSP.model.ExportModel("fileName.lp");
        }
        /// <summary>
        /// My solve
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        public void Solver(Solution solution, int numMachines, int numJobs)
        {
            model.Solve();

            if (model.GetStatus() == Cplex.Status.Optimal)
            {
                solution._isFeasible = true;
                solution._isInteger = true;
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine("The objective objValue of RMP is： " + model.ObjValue);
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine();
                solution.objValue = model.ObjValue;

                solution.yks = new List<double[]>();
                for (int k = 0; k < numMachines; k++)
                {
                    double[] ys = model.GetValues(varYks[k].ToArray());
                    solution.yks.Add(ys);
                }

                solution.dualsOfJobs = new double[numJobs];
                solution.dualsOfMachines = new double[numMachines];
                solution.dualsOfLmSRCsOfVertex = new List<double>();

                solution.CheckIntegerality();
            }
            else
            {
                solution._isFeasible = false;
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine("The problem is infeasible");
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine();
            }
        }
        /// <summary>
        ///  Add lm-SRCs
        /// </summary>
        /// <param name="numMachines"></param>
        /// <param name="numJobs"></param>
        /// <param name="lmSRCs"></param>
        /// <param name="UPMSP"></param>
        /// <param name="usedPartialScheduleSet"></param>
        public void AddLmSRCs(int numMachines, List<LmSRCOfVertex> lmSRCs, IPSolver UPMSP)
        {
            //UPMSP.constaintOfLmSRC = new List<IRange>();
            for (int o = 0; o < lmSRCs.Count; o++)
            {
                ILinearNumExpr constaintFunction = model.LinearNumExpr();
                for (int k = 0; k < numMachines; k++)
                {
                    for (int s = 0; s < lmSRCs[o].coeff[k].Count; s++)
                    {
                        constaintFunction.AddTerm(lmSRCs[o].coeff[k][s], varYks[k][s]);
                    }
                }
                UPMSP.constaintOfLmSRC.Add(UPMSP.model.AddLe(constaintFunction, 1));
            }
        }
    }
}
