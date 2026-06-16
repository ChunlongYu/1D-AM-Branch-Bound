using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    public class PartialSchedule
    {
        public List<int> setOfProcessedJobs { get; set; }
        public double TWCT { get; set; }
        public double[] vectorOfProcessedJob { get; set; }
        public double time { get; set; }
        public PartialSchedule() { }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="partialSchedule"></param>
        public PartialSchedule(PartialSchedule partialSchedule)
        {
            setOfProcessedJobs = new List<int>(partialSchedule.setOfProcessedJobs);
            TWCT = partialSchedule.TWCT;
            vectorOfProcessedJob = new double[partialSchedule.vectorOfProcessedJob.Length];
            Array.Copy(partialSchedule.vectorOfProcessedJob, vectorOfProcessedJob, vectorOfProcessedJob.Length);
            time = partialSchedule.time;
        }
        /// <summary>
        /// Print the set of processed jobs
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string result = "";
            foreach (var item in setOfProcessedJobs)
            {
                result += item + " ";
            }
            return result;
        }
    }
    public class Solution
    {
        public List<List<PartialSchedule>> usedPartialScheduleSet { get; set; }
        public List<LmSRCOfVertex> usedLmSRCs { get; set; }
        public List<double[]> yks { get; set; }
        public double[] dualsOfJobs { get; set; }
        public double[] dualsOfMachines { get; set; }
        public List<double> dualsOfLmSRCsOfVertex { get; set; }
        public double objValue { get; set; }
        public bool _isInteger { get; set; }
        public bool _isFeasible { get; set; }
        public List<BucketGraph> forwardBucketGraphs { get; set; }
        public List<BucketGraph> backwardBucketGraphs { get; set; }
        public List<PartialSchedule> bestSchedules { get; set; }
        public double makespan { get; set; }

        public Solution() { }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="solution"></param>
        public Solution(Solution solution)
        {
            usedPartialScheduleSet = new List<List<PartialSchedule>>();
            for (int i = 0; i < solution.usedPartialScheduleSet.Count; i++)
            {
                usedPartialScheduleSet.Add(new List<PartialSchedule>());
                for (int j = 0; j < solution.usedPartialScheduleSet[i].Count; j++)
                {
                    usedPartialScheduleSet[i].Add(new PartialSchedule(solution.usedPartialScheduleSet[i][j]));
                }
            }
            usedLmSRCs = new List<LmSRCOfVertex>();
            for (int i = 0; i < solution.usedLmSRCs.Count; i++)
            {
                usedLmSRCs.Add(new LmSRCOfVertex(solution.usedLmSRCs[i]));
            }
            yks = new List<double[]>();
            for (int i = 0; i < solution.yks.Count; i++)
            {
                double[] yk = new double[solution.yks[i].Length];
                Array.Copy(solution.yks[i], yk, yk.Length);
                yks.Add(yk);
            }
            dualsOfJobs = new double[solution.dualsOfJobs.Length];
            Array.Copy(solution.dualsOfJobs, dualsOfJobs, dualsOfJobs.Length);
            dualsOfMachines = new double[solution.dualsOfMachines.Length];
            Array.Copy(solution.dualsOfMachines, dualsOfMachines, dualsOfMachines.Length);
            dualsOfLmSRCsOfVertex = new List<double>();
            for (int i = 0; i < solution.dualsOfLmSRCsOfVertex.Count; i++)
            {
                dualsOfLmSRCsOfVertex.Add(solution.dualsOfLmSRCsOfVertex[i]);
            }

            objValue = solution.objValue;
            _isInteger = solution._isInteger;
            _isFeasible = solution._isFeasible;

            forwardBucketGraphs = new List<BucketGraph>();
            for (int i = 0; i < solution.forwardBucketGraphs.Count; i++)
            {
                forwardBucketGraphs.Add(new BucketGraph(solution.forwardBucketGraphs[i]));
            }
            backwardBucketGraphs = new List<BucketGraph>();
            for (int i = 0; i < solution.backwardBucketGraphs.Count; i++)
            {
                backwardBucketGraphs.Add(new BucketGraph(solution.backwardBucketGraphs[i]));
            }
            bestSchedules = new List<PartialSchedule>();
            for (int i = 0; i < solution.bestSchedules.Count; i++)
            {
                bestSchedules.Add(new PartialSchedule(solution.bestSchedules[i]));
            }
            makespan = solution.makespan;
        }
        /// <summary>
        /// Remove partial schedules that job l is scheduled immediately after job h 
        /// </summary>
        /// <param name="machineID"></param>
        /// <param name="precedeJob"></param>
        /// <param name="succeedJob"></param>
        public void UpdatePartialSchedulesWithIJ_0(int machineID, int precedeJob, int succeedJob)
        {
            for (int s = 0; s < usedPartialScheduleSet[machineID].Count; s++)
            {
                for (int j = 0; j < usedPartialScheduleSet[machineID][s].setOfProcessedJobs.Count - 1; j++)
                {
                    if ((usedPartialScheduleSet[machineID][s].setOfProcessedJobs[j] == precedeJob) && (usedPartialScheduleSet[machineID][s].setOfProcessedJobs[j + 1] == succeedJob))
                    {
                        usedPartialScheduleSet[machineID].RemoveAt(s);
                        for (int c = 0; c < usedLmSRCs.Count; c++)
                        {
                            usedLmSRCs[c].coeff[machineID].RemoveAt(s);
                        }
                        s--;
                        break;
                    }
                }
            }
        }
        /// <summary>
        ///  Remove partial schedules that schedule job l immediately after a job other than job h or schedule a job other than h immediately before job l
        /// </summary>
        /// <param name="machineID"></param>
        /// <param name="precedeJob"></param>
        /// <param name="succeedJob"></param>
        public void UpdatePartialSchedulesWithIJ_1(int machineID, int precedeJob, int succeedJob)
        {
            for (int s = 0; s < usedPartialScheduleSet[machineID].Count; s++)
            {
                for (int j = 0; j < usedPartialScheduleSet[machineID][s].setOfProcessedJobs.Count - 1; j++)
                {
                    if (((usedPartialScheduleSet[machineID][s].setOfProcessedJobs[j] == precedeJob) && (usedPartialScheduleSet[machineID][s].setOfProcessedJobs[j + 1] != succeedJob)) || ((usedPartialScheduleSet[machineID][s].setOfProcessedJobs[j] != precedeJob) && (usedPartialScheduleSet[machineID][s].setOfProcessedJobs[j + 1] == succeedJob)))
                    {
                        usedPartialScheduleSet[machineID].RemoveAt(s);
                        for (int c = 0; c < usedLmSRCs.Count; c++)
                        {
                            usedLmSRCs[c].coeff[machineID].RemoveAt(s);
                        }
                        s--;
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Check whether the solution is integer or not
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public void CheckIntegerality()
        {
            _isInteger = true;
            if (_isFeasible == true)
            {
                bestSchedules = new List<PartialSchedule>();
                makespan = 0;
                for (int k = 0; k < yks.Count; k++)
                {
                    for (int j = 0; j < yks[k].Length; j++)
                    {
                        if ((yks[k][j] > 0.00001) && (yks[k][j] < 0.99999))
                        {
                            _isInteger = false;
                            break;
                        }
                        else if (yks[k][j] >= 0.99999)
                        {
                            bestSchedules.Add(usedPartialScheduleSet[k][j]);
                        }
                    }
                    if (_isInteger == false) break;
                }
                foreach (PartialSchedule partialSchedule in bestSchedules)
                {
                    if (partialSchedule.time > makespan)
                    {
                        makespan = partialSchedule.time;
                    }
                }
            }
        }
    }

    public class LmSRCOfVertex
    {
        public int id { get; set; }
        public List<int> subSet { get; set; }
        public List<List<int>> memorySet { get; set; }
        public List<List<double>> coeff { get; set; }
        public double violation { get; set; }
        //public double dualValue { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public LmSRCOfVertex() { }
        /// <summary>
        ///Copy constructor
        /// </summary>
        /// <param name="lmSRC"></param>
        public LmSRCOfVertex(LmSRCOfVertex lmSRC)
        {
            id = lmSRC.id;
            subSet = new List<int>();
            for (int i = 0; i < lmSRC.subSet.Count; i++)
            {
                subSet.Add(lmSRC.subSet[i]);
            }
            memorySet = new List<List<int>>();
            for (int i = 0; i < lmSRC.memorySet.Count; i++)
            {
                memorySet.Add(new List<int>());
                for (int j = 0; j < lmSRC.memorySet[i].Count; j++)
                {
                    memorySet[i].Add(lmSRC.memorySet[i][j]);
                }
            }
            coeff = new List<List<double>>();
            for (int i = 0; i < lmSRC.coeff.Count; i++)
            {
                coeff.Add(new List<double>());
                for (int j = 0; j < lmSRC.coeff[i].Count; j++)
                {
                    coeff[i].Add(lmSRC.coeff[i][j]);
                }
            }
            violation = lmSRC.violation;
            //dualValue = lmSRC.dualValue;
        }
        /// <summary>
        /// Print lm-SRC
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "id = " + id + ", subSet = ";
            for (int i = 0; i < subSet.Count; i++)
            {
                str += subSet[i] + " ";
            }
            str += ", memorySet = ";
            for (int i = 0; i < memorySet.Count; i++)
            {
                for (int j = 0; j < memorySet[i].Count; j++)
                {
                    str += memorySet[i][j] + " ";
                }
                str += "; ";
            }
            str += ", coeff = ";
            for (int i = 0; i < coeff.Count; i++)
            {
                for (int j = 0; j < coeff[i].Count; j++)
                {
                    str += coeff[i][j] + " ";
                }
                str += "; ";
            }
            //str += ", dualValue = " + dualValue;
            return str;
        }
    }

    public class Node : Solution
    {
        public List<CandidateBranchVariables> branchVariables { get; set; }
        public Node() { }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="node"></param>
        public Node(Node node)
        {
            usedPartialScheduleSet = new List<List<PartialSchedule>>();
            for (int i = 0; i < node.usedPartialScheduleSet.Count; i++)
            {
                usedPartialScheduleSet.Add(new List<PartialSchedule>());
                for (int j = 0; j < node.usedPartialScheduleSet[i].Count; j++)
                {
                    usedPartialScheduleSet[i].Add(new PartialSchedule(node.usedPartialScheduleSet[i][j]));
                }
            }

            usedLmSRCs = new List<LmSRCOfVertex>();
            for (int i = 0; i < node.usedLmSRCs.Count; i++)
            {
                usedLmSRCs.Add(new LmSRCOfVertex(node.usedLmSRCs[i]));
            }

            yks = new List<double[]>();
            for (int i = 0; i < node.yks.Count; i++)
            {
                double[] yk = new double[node.yks[i].Length];
                Array.Copy(node.yks[i], yk, yk.Length);
                yks.Add(yk);
            }

            dualsOfJobs = new double[node.dualsOfJobs.Length];
            Array.Copy(node.dualsOfJobs, dualsOfJobs, dualsOfJobs.Length);

            dualsOfMachines = new double[node.dualsOfMachines.Length];
            Array.Copy(node.dualsOfMachines, dualsOfMachines, dualsOfMachines.Length);

            dualsOfLmSRCsOfVertex = new List<double>();
            for (int i = 0; i < node.dualsOfLmSRCsOfVertex.Count; i++)
            {
                dualsOfLmSRCsOfVertex.Add(node.dualsOfLmSRCsOfVertex[i]);
            }

            objValue = node.objValue;
            _isInteger = node._isInteger;
            _isFeasible = node._isFeasible;

            forwardBucketGraphs = new List<BucketGraph>();
            for (int i = 0; i < node.forwardBucketGraphs.Count; i++)
            {
                forwardBucketGraphs.Add(new BucketGraph(node.forwardBucketGraphs[i]));
            }

            backwardBucketGraphs = new List<BucketGraph>();
            for (int i = 0; i < node.backwardBucketGraphs.Count; i++)
            {
                backwardBucketGraphs.Add(new BucketGraph(node.backwardBucketGraphs[i]));
            }

            //fractionalQijForBranching = new List<int[]>();
            //for (int i = 0; i < node.fractionalQijForBranching.Count; i++)
            //{
            //    int[] qij = new int[node.fractionalQijForBranching[i].Length];
            //    Array.Copy(node.fractionalQijForBranching[i], qij, qij.Length);
            //    fractionalQijForBranching.Add(qij);
            //}

            //fractionalXkijForBranching = new List<int[]>();
            //for (int i = 0; i < node.fractionalXkijForBranching.Count; i++)
            //{
            //    int[] xkij = new int[node.fractionalXkijForBranching[i].Length];
            //    Array.Copy(node.fractionalXkijForBranching[i], xkij, xkij.Length);
            //    fractionalXkijForBranching.Add(xkij);
            //}

            branchVariables = new List<CandidateBranchVariables>();
            for (int i = 0; i < node.branchVariables.Count; i++)
            {
                branchVariables.Add(new CandidateBranchVariables(node.branchVariables[i]));
            }

            bestSchedules = new List<PartialSchedule>();
            for (int i = 0; i < node.bestSchedules.Count; i++)
            {
                bestSchedules.Add(new PartialSchedule(node.bestSchedules[i]));
            }

            makespan = node.makespan;
        }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="node"></param>
        public Node(Solution solution)
        {
            usedPartialScheduleSet = new List<List<PartialSchedule>>();
            for (int i = 0; i < solution.usedPartialScheduleSet.Count; i++)
            {
                usedPartialScheduleSet.Add(new List<PartialSchedule>());
                for (int j = 0; j < solution.usedPartialScheduleSet[i].Count; j++)
                {
                    usedPartialScheduleSet[i].Add(new PartialSchedule(solution.usedPartialScheduleSet[i][j]));
                }
            }

            usedLmSRCs = new List<LmSRCOfVertex>();
            for (int i = 0; i < solution.usedLmSRCs.Count; i++)
            {
                usedLmSRCs.Add(new LmSRCOfVertex(solution.usedLmSRCs[i]));
            }

            yks = new List<double[]>();
            for (int i = 0; i < solution.yks.Count; i++)
            {
                double[] yk = new double[solution.yks[i].Length];
                Array.Copy(solution.yks[i], yk, yk.Length);
                yks.Add(yk);
            }

            dualsOfJobs = new double[solution.dualsOfJobs.Length];
            Array.Copy(solution.dualsOfJobs, dualsOfJobs, dualsOfJobs.Length);

            dualsOfMachines = new double[solution.dualsOfMachines.Length];
            Array.Copy(solution.dualsOfMachines, dualsOfMachines, dualsOfMachines.Length);

            dualsOfLmSRCsOfVertex = new List<double>();
            for (int i = 0; i < solution.dualsOfLmSRCsOfVertex.Count; i++)
            {
                dualsOfLmSRCsOfVertex.Add(solution.dualsOfLmSRCsOfVertex[i]);
            }

            bestSchedules = new List<PartialSchedule>();
            for (int i = 0; i < solution.bestSchedules.Count; i++)
            {
                bestSchedules.Add(new PartialSchedule(solution.bestSchedules[i]));
            }

            forwardBucketGraphs = new List<BucketGraph>();
            for (int i = 0; i < solution.forwardBucketGraphs.Count; i++)
            {
                forwardBucketGraphs.Add(new BucketGraph(solution.forwardBucketGraphs[i]));
            }

            backwardBucketGraphs = new List<BucketGraph>();
            for (int i = 0; i < solution.backwardBucketGraphs.Count; i++)
            {
                backwardBucketGraphs.Add(new BucketGraph(solution.backwardBucketGraphs[i]));
            }

            makespan = solution.makespan;
            objValue = solution.objValue;
            _isInteger = solution._isInteger;
            _isFeasible = solution._isFeasible;
        }

        /// <summary>
        /// Print the node
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "objValue: " + objValue + " ";
            str += "isInteger: " + _isInteger + " ";
            str += "isOptimal: " + _isFeasible + " ";
            //str += "fractionalQijForBranching: " + fractionalQijForBranching.Count + " ";
            //str += "fractionalXkijForBranching: " + fractionalXkijForBranching.Count + " ";
            return str;
        }

    }
    public class ForwardLabel : PartialSchedule
    {
        public int lastJob { get; set; }
        public double reducedCost { get; set; }
        public List<double> lmSRCsState { get; set; }
        public List<int> setOfBucketIndex { get; set; }
        public bool _isExtended { get; set; }

        public ForwardLabel()
        {
            this.lastJob = -1;
            this.time = 0;
            this.reducedCost = 0;
            this.lmSRCsState = new List<double>();
            this.setOfProcessedJobs = new List<int>();
            this.setOfBucketIndex = new List<int>();
            this._isExtended = false;
        }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="label"></param>
        public ForwardLabel(ForwardLabel label)
        {
            this.lastJob = label.lastJob;
            this.time = label.time;
            this.reducedCost = label.reducedCost;
            this.lmSRCsState = new List<double>();
            for (int i = 0; i < label.lmSRCsState.Count; i++)
            {
                this.lmSRCsState.Add(label.lmSRCsState[i]);
            }
            this.setOfProcessedJobs = new List<int>();
            for (int i = 0; i < label.setOfProcessedJobs.Count; i++)
            {
                this.setOfProcessedJobs.Add(label.setOfProcessedJobs[i]);
            }
            this.setOfBucketIndex = new List<int>();
            for (int i = 0; i < label.setOfBucketIndex.Count; i++)
            {
                this.setOfBucketIndex.Add(label.setOfBucketIndex[i]);
            }
            this._isExtended = label._isExtended;
        }
        /// <summary>
        /// Print the label
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "lastJob: " + lastJob + " ";
            str += "completeTime: " + time + " ";
            str += "reducedCost: " + reducedCost + " ";
            str += "processed jobs: ";
            foreach (var item in setOfProcessedJobs)
            {
                str += item + " ";
            }
            //str += "lmSRCsState: ";
            //for (int i = 0; i < lmSRCsState.Count; i++)
            //{
            //    str += lmSRCsState[i] + " ";
            //}
            return str;
        }
    }

    public class BackwardLabel : PartialSchedule
    {
        public int firstJob { get; set; }
        public double duration { get; set; }
        public double cumulativeWeight { get; set; }
        public double baseReducedCost { get; set; }
        public List<double> lmSRCsState { get; set; }
        public List<int> setOfBucketIndex { get; set; }
        public bool _isExtended { get; set; }

        public BackwardLabel()
        {
            this.firstJob = -1;
            this.duration = 0;
            this.time = 0;
            this.cumulativeWeight = 0;
            this.baseReducedCost = 0;
            this.lmSRCsState = new List<double>();
            this.setOfProcessedJobs = new List<int>();
            this.setOfBucketIndex = new List<int>();
            this._isExtended = false;
        }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="label"></param>
        public BackwardLabel(BackwardLabel label)
        {
            this.firstJob = label.firstJob;
            this.duration = label.duration;
            this.time = label.time;
            this.cumulativeWeight = label.cumulativeWeight;
            this.baseReducedCost = label.baseReducedCost;
            this.lmSRCsState = new List<double>();
            for (int i = 0; i < label.lmSRCsState.Count; i++)
            {
                this.lmSRCsState.Add(label.lmSRCsState[i]);
            }
            this.setOfProcessedJobs = new List<int>();
            for (int i = 0; i < label.setOfProcessedJobs.Count; i++)
            {
                this.setOfProcessedJobs.Add(label.setOfProcessedJobs[i]);
            }
            this.setOfBucketIndex = new List<int>();
            for (int i = 0; i < label.setOfBucketIndex.Count; i++)
            {
                this.setOfBucketIndex.Add(label.setOfBucketIndex[i]);
            }
            this._isExtended = label._isExtended;
        }
        /// <summary>
        /// Print the label
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "firstJob: " + firstJob + " ";
            str += "duration: " + duration + " ";
            str += "cumulativeWeight: " + cumulativeWeight + " ";
            str += "baseReducedCost: " + baseReducedCost + " ";
            str += "processed jobs: ";
            foreach (var item in setOfProcessedJobs)
            {
                str += item + " ";
            }
            //str += "lmSRCsState: ";
            //for (int i = 0; i < lmSRCsState.Count; i++)
            //{
            //    str += lmSRCsState[i] + " ";
            //}
            return str;
        }
    }


    public class BucketGraph
    {
        public List<Bucket> buckets { get; set; }
        public Dictionary<Bucket, IList<BucketArc>> adjListOfBucketArcs { get; set; }
        public Dictionary<Bucket, IList<Bucket>> adjComponentWiseSmallerBuckets { get; set; }
        public List<StronglyConnectedComponent> stronglyConnectedComponents { get; set; }
        public List<ForwardLabel> nonDominatedLabelsSet { get; set; }
        public List<BackwardLabel> nonDominatedBackwardLabelsSet { get; set; }
        public List<DynamicShrinkBound> dynamicShrinkBound { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public BucketGraph()
        {
            buckets = new List<Bucket>();
            adjListOfBucketArcs = new Dictionary<Bucket, IList<BucketArc>>();
            adjComponentWiseSmallerBuckets = new Dictionary<Bucket, IList<Bucket>>();
            stronglyConnectedComponents = new List<StronglyConnectedComponent>();
            nonDominatedLabelsSet = new List<ForwardLabel>();
            nonDominatedBackwardLabelsSet = new List<BackwardLabel>();
            dynamicShrinkBound = new List<DynamicShrinkBound>();
        }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="bucketGraph"></param>
        public BucketGraph(BucketGraph bucketGraph)
        {
            this.buckets = new List<Bucket>(bucketGraph.buckets);
            this.adjListOfBucketArcs = new Dictionary<Bucket, IList<BucketArc>>();
            foreach (KeyValuePair<Bucket, IList<BucketArc>> kvp in bucketGraph.adjListOfBucketArcs)
            {
                this.adjListOfBucketArcs[kvp.Key] = new List<BucketArc>(kvp.Value);
            }
            this.adjComponentWiseSmallerBuckets = new Dictionary<Bucket, IList<Bucket>>();
            foreach (KeyValuePair<Bucket, IList<Bucket>> kvp in bucketGraph.adjComponentWiseSmallerBuckets)
            {
                this.adjComponentWiseSmallerBuckets[kvp.Key] = new List<Bucket>(kvp.Value);
            }
            this.stronglyConnectedComponents = new List<StronglyConnectedComponent>();
            foreach (StronglyConnectedComponent scc in bucketGraph.stronglyConnectedComponents)
            {
                this.stronglyConnectedComponents.Add(new StronglyConnectedComponent(scc.bucket, scc.numOfArcs, scc.topologyOrder));
            }
            this.nonDominatedBackwardLabelsSet = new List<BackwardLabel>();
            foreach (BackwardLabel label in bucketGraph.nonDominatedBackwardLabelsSet) 
            { 
                this.nonDominatedBackwardLabelsSet.Add(label);
            }
            this.nonDominatedLabelsSet = new List<ForwardLabel>();
            foreach (ForwardLabel label in bucketGraph.nonDominatedLabelsSet)
            {
                this.nonDominatedLabelsSet.Add(new ForwardLabel(label));
            }
            this.dynamicShrinkBound = new List<DynamicShrinkBound>();
            foreach (DynamicShrinkBound bound in bucketGraph.dynamicShrinkBound)
            {
                this.dynamicShrinkBound.Add(new DynamicShrinkBound(bound));
            }
        }
        /// <summary>
        ///  Initial the forward bucket graph
        /// </summary>
        /// <param name="machineID"></param>
        /// <param name="numJobs"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="processingTimes"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="succeedJobOrderingRestrictionOnAllMachines"></param>
        public void InitialForwardBucketGraph(int machineID, UPMSPInstances instance, Parameters  parameters)
        {
            buckets = new List<Bucket>();
            adjListOfBucketArcs = new Dictionary<Bucket, IList<BucketArc>>();
            adjComponentWiseSmallerBuckets = new Dictionary<Bucket, IList<Bucket>>();
            stronglyConnectedComponents = new List<StronglyConnectedComponent>();
            nonDominatedLabelsSet = new List<ForwardLabel>();

            // Add bucket
            int[] numOfBucketOnOneVertex = new int[instance.numJobs + 2];
            numOfBucketOnOneVertex[0] = 1;
            numOfBucketOnOneVertex[instance.numJobs + 1] = 1;
            for (int j = 1; j <= instance.numJobs; j++)
            {
                numOfBucketOnOneVertex[j] = parameters.numOfBucketOnOneVertex;
            }
            double[] stepSize = new double[instance.numJobs + 2];
            for (int i = 0; i < dynamicShrinkBound.Count; i++)
            {
                stepSize[i] = (dynamicShrinkBound[i].upperBound - dynamicShrinkBound[i].lowerBound) / numOfBucketOnOneVertex[i];
            }

            // Add the job buckets
            for (int j = 0; j < instance.numJobs + 2; j++)
            {
                for (int n = 0; n < numOfBucketOnOneVertex[j]; n++)
                {
                    Bucket bucket = new Bucket();
                    bucket.vertex = j;
                    bucket.index = n;
                    bucket.stepsize = stepSize[j];
                    bucket.lb = bucket.index * bucket.stepsize;
                    bucket.ub = bucket.lb + bucket.stepsize;
                    bucket.minReducedCost = double.MaxValue;
                    bucket.labelSet = new List<ForwardLabel>();
                    AddBucket(bucket);
                    if (n > 0)
                    {
                        int indexOfAdjComponentWiseSmallerBucket = buckets.Count - 2;
                        AddComponentWiseSmallerBuckets(bucket, buckets[indexOfAdjComponentWiseSmallerBucket]);
                    }
                }
            }

            // Add the arcs from the job buckets  to the job buckets
            for (int i = 0; i < buckets.Count; i++)
            {
                for (int j = 0; j < dynamicShrinkBound[buckets[i].vertex].succeedOrderingRestriction.Count; j++)
                {
                    int succeedJob = dynamicShrinkBound[buckets[i].vertex].succeedOrderingRestriction[j];
                    int iterator = 0;
                    for (int iter = 0; iter < succeedJob; iter++)
                    {
                        iterator += numOfBucketOnOneVertex[iter];
                    }
                    double processingTime = 0;
                    if ((succeedJob != 0) && (succeedJob != instance.numJobs + 1))
                    {
                        processingTime = instance.processingTimes[succeedJob - 1, machineID];
                    }

                    if (buckets[i].lb + processingTime > dynamicShrinkBound[succeedJob].upperBound) continue;
                    for (int h = 0; h < numOfBucketOnOneVertex[succeedJob]; h++)
                    {
                        if (h == 0)
                        {
                            if ((buckets[i].lb + processingTime >= buckets[iterator + h].lb) && (buckets[i].lb + processingTime <= buckets[iterator + h].ub))
                            {
                                AddBucketArc(buckets[i], buckets[iterator + h], processingTime);
                            }
                        }
                        else 
                        {
                            if ((buckets[i].lb + processingTime > buckets[iterator + h].lb) && (buckets[i].lb + processingTime <= buckets[iterator + h].ub))
                            {
                                AddBucketArc(buckets[i], buckets[iterator + h], processingTime);
                            }
                        }
                    }
                }
            }

            SortSCCs(instance, parameters);
        }
        /// <summary>
        /// Initial the backward bucket graph
        /// </summary>
        /// <param name="machineID"></param>
        /// <param name="numJobs"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        /// <param name="processingTimes"></param>
        /// <param name="upperBoundOfCompletionTime"></param>
        /// <param name="precedeJobOrderingRestrictionOnAllMachines"></param>
        public void InitialBackwardBucketGraph(int machineID, UPMSPInstances instance,  Parameters parameters)
        {
            // Add bucket
            int[] numOfBucketOnOneVertex = new int[instance.numJobs + 2];
            numOfBucketOnOneVertex[0] = 1;
            numOfBucketOnOneVertex[instance.numJobs + 1] = 1;
            for (int j = 1; j <= instance.numJobs; j++)
            {
                numOfBucketOnOneVertex[j] = parameters.numOfBucketOnOneVertex;
            }

            double[] stepSize = new double[instance.numJobs + 2];
            for (int i = 0; i < dynamicShrinkBound.Count; i++)
            {
                stepSize[i] = (dynamicShrinkBound[i].upperBound - dynamicShrinkBound[i].lowerBound) / numOfBucketOnOneVertex[i];
            }

            // Add the job buckets
            for (int j = 0; j < instance.numJobs + 2; j++)
            {
                for (int n = 0; n < numOfBucketOnOneVertex[j]; n++)
                {
                    Bucket bucket = new Bucket();
                    bucket.vertex = j;
                    bucket.index = n;
                    bucket.stepsize = stepSize[j];
                    bucket.ub = dynamicShrinkBound[j].upperBound - bucket.index * bucket.stepsize;
                    bucket.lb = bucket.ub - bucket.stepsize;
                    if ((bucket.lb < -0.0001) && (bucket.lb > 0.0001))
                    {
                        bucket.lb = 0;
                    }
                    bucket.minReducedCost = double.MaxValue;
                    bucket.labelSet = new List<ForwardLabel>();
                    AddBucket(bucket);
                    if (n > 0)
                    {
                        int indexOfAdjComponentWiseSmallerBucket = buckets.Count - 2;
                        AddComponentWiseSmallerBuckets(bucket, buckets[indexOfAdjComponentWiseSmallerBucket]);
                    }
                }
            }

            // Add the arcs from the job buckets  to the job buckets
            for (int i = 0; i < buckets.Count; i++)
            {
                for (int j = 0; j < dynamicShrinkBound[buckets[i].vertex].precedeOrderingRestriction.Count; j++)
                {
                    int precedJob = dynamicShrinkBound[buckets[i].vertex].precedeOrderingRestriction[j];
                    int iterator = 0;
                    for (int iter = 0; iter < precedJob; iter++)
                    {
                        iterator += numOfBucketOnOneVertex[iter];
                    }
                    double processingTime = 0;
                    if ((precedJob != 0) && (precedJob != instance.numJobs + 1))
                    {
                        processingTime = instance.processingTimes[precedJob - 1, machineID];
                    }
                    if (buckets[i].ub - processingTime < dynamicShrinkBound[precedJob].lowerBound) continue;
                    for (int h = 0; h < numOfBucketOnOneVertex[precedJob]; h++)
                    {
                        double value = buckets[i].ub - processingTime;
                        if (value > buckets[iterator + h].ub) 
                        {
                            value = buckets[iterator + h].ub;
                        }
                        if (h == 0)
                        {
                            if ((value <= buckets[iterator + h].ub) && (value >= buckets[iterator + h].lb))
                            {
                                AddBucketArc(buckets[i], buckets[iterator + h], processingTime);
                            }
                        }
                        else 
                        {
                            if ((value < buckets[iterator + h].ub) && (value >= buckets[iterator + h].lb))
                            {
                                AddBucketArc(buckets[i], buckets[iterator + h], processingTime);
                            }
                        }
                    }
                }
            }

            SortSCCs_backward(instance, parameters);
        }
        /// <summary>
        /// Add bucket
        /// </summary>
        /// <param name="bucket"></param>
        public void AddBucket(Bucket bucket)
        {
            buckets.Add(bucket);
            if (!adjListOfBucketArcs.ContainsKey(bucket))
            {
                adjListOfBucketArcs[bucket] = new List<BucketArc>();
            }
            if (!adjComponentWiseSmallerBuckets.ContainsKey(bucket))
            {
                adjComponentWiseSmallerBuckets[bucket] = new List<Bucket>();
            }
        }
        /// <summary>
        /// Add adjacent component-wise smaller buckets and its arc
        /// </summary>
        /// <param name="bucket"></param>
        /// <param name="smallerBucket"></param>
        public void AddComponentWiseSmallerBuckets(Bucket bucket, Bucket smallerBucket)
        {
            if (!adjComponentWiseSmallerBuckets.ContainsKey(bucket))
            {
                adjComponentWiseSmallerBuckets[bucket] = new List<Bucket>();
            }
            adjComponentWiseSmallerBuckets[bucket].Add(smallerBucket);
        }
        /// <summary>
        ///Add arc from tail to head
        /// </summary>
        /// <param name="tail"></param>
        /// <param name="head"></param>
        /// <param name="processTimeOnArc"></param>
        public void AddBucketArc(Bucket tail, Bucket head, double processTimeOnArc)
        {
            if (!adjListOfBucketArcs.ContainsKey(tail))
            {
                adjListOfBucketArcs[tail] = new List<BucketArc>();
            }
            int[] arc = new int[2];
            arc[0] = tail.vertex;
            arc[1] = head.vertex;
            adjListOfBucketArcs[tail].Add(new BucketArc(head, arc, processTimeOnArc, new List<Bucket>()));
        }
        /// <summary>
        ///  Delete original arcs from the bucket graph
        /// </summary>
        /// <param name="tailVertex"></param>
        /// <param name="headVertex"></param>
        public void UpdateBucketArcsFixingIJ_0(int tailVertex, int headVertex)
        {
            foreach (Bucket bucket in buckets)
            {
                if (bucket.vertex != tailVertex) continue;
                for (int a = 0; a < adjListOfBucketArcs[bucket].Count; a++)
                {
                    BucketArc bucketArc = adjListOfBucketArcs[bucket][a];
                    if (bucketArc.headBucket.vertex == headVertex)
                    {
                        adjListOfBucketArcs[bucket].Remove(bucketArc);
                        a--;
                    }
                }
            }
        }
        /// <summary>
        /// Update job ordering restriction IJ = 0
        /// </summary>
        /// <param name="tailVertex"></param>
        /// <param name="headVertex"></param>
        public void UpdateJobOrderingRestrictionIJ_0(int tailVertex, int headVertex)
        {
            for (int i = 0; i < dynamicShrinkBound[tailVertex].succeedOrderingRestriction.Count; i++)
            {
                if (dynamicShrinkBound[tailVertex].succeedOrderingRestriction[i] == headVertex)
                {
                    dynamicShrinkBound[tailVertex].succeedOrderingRestriction.RemoveAt(i);
                    break;
                }
            }

            for (int i = 0; i < dynamicShrinkBound[headVertex].precedeOrderingRestriction.Count; i++)
            {
                if (dynamicShrinkBound[headVertex].precedeOrderingRestriction[i] == tailVertex)
                {
                    dynamicShrinkBound[headVertex].precedeOrderingRestriction.RemoveAt(i);
                    break;
                }
            }
        }
        /// <summary>
        /// Delete original arcs from the bucket graph
        /// </summary>
        /// <param name="tailVertex"></param>
        /// <param name="headVertex"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        public void UpdateBucketArcsFixingIJ_1(int tailVertex, int headVertex, int numOfBucketOnOneVertex)
        {
            for (int b = (tailVertex - 1) * numOfBucketOnOneVertex + 1; b < tailVertex * numOfBucketOnOneVertex + 1; b++)
            {
                Bucket bucket = buckets[b];
                for (int a = 0; a < adjListOfBucketArcs[bucket].Count; a++)
                {
                    BucketArc bucketArc = adjListOfBucketArcs[bucket][a];
                    if (bucketArc.headBucket.vertex != headVertex)
                    {
                        adjListOfBucketArcs[bucket].RemoveAt(a);
                        a--;
                    }
                }
            }
        }
        /// <summary>
        /// Update job ordering restriction IJ = 1
        /// </summary>
        /// <param name="tailVertex"></param>
        /// <param name="headVertex"></param>
        public void UpdateJobOrderingRestrictionIJ_1(int tailVertex, int headVertex)
        {
            for (int i = 0; i < dynamicShrinkBound[tailVertex].succeedOrderingRestriction.Count; i++)
            {
                if (dynamicShrinkBound[tailVertex].succeedOrderingRestriction[i] != headVertex)
                {
                    dynamicShrinkBound[tailVertex].succeedOrderingRestriction.RemoveAt(i);
                    i--;
                }
            }

            for (int i = 0; i < dynamicShrinkBound[headVertex].precedeOrderingRestriction.Count; i++)
            {
                if (dynamicShrinkBound[headVertex].precedeOrderingRestriction[i] != tailVertex)
                {
                    dynamicShrinkBound[headVertex].precedeOrderingRestriction.RemoveAt(i);
                    i--;
                }
            }
        }
        /// <summary>
        /// Delete original arcs from the bucket graph
        /// </summary>
        /// <param name="tailVertex"></param>
        /// <param name="headVertex"></param>
        /// <param name="numOfBucketOnOneVertex"></param>
        public void UpdateBucketArcsFixingKIJ_1(int tailVertex, int headVertex)
        {
            foreach (Bucket bucket in buckets)
            {
                if (bucket.vertex == tailVertex)
                {
                    adjListOfBucketArcs[bucket] = new List<BucketArc>();
                }
                else
                {
                    for (int a = 0; a < adjListOfBucketArcs[bucket].Count; a++)
                    {
                        BucketArc bucketArc = adjListOfBucketArcs[bucket][a];
                        if (bucketArc.headBucket.vertex == headVertex)
                        {
                            adjListOfBucketArcs[bucket].Remove(bucketArc);
                            a--;
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Update job ordering restriction KIJ = 1
        /// </summary>
        /// <param name="tailVertex"></param>
        /// <param name="headVertex"></param>
        public void UpdateJobOrderingRestrictionKIJ_1(int tailVertex, int headVertex, int numJobs)
        {
            for (int j = 0; j < numJobs; j++)
            {
                if (j == tailVertex - 1)
                {
                    dynamicShrinkBound[tailVertex].succeedOrderingRestriction = new List<int>();
                }
                else
                {
                    for (int i = 0; i < dynamicShrinkBound[j + 1].succeedOrderingRestriction.Count; i++)
                    {
                        if (dynamicShrinkBound[j + 1].succeedOrderingRestriction[i] == headVertex)
                        {
                            dynamicShrinkBound[j + 1].succeedOrderingRestriction.RemoveAt(i);
                            break;
                        }
                    }
                }
            }

            for (int j = 0; j < numJobs; j++)
            {
                if (j == headVertex - 1)
                {
                    dynamicShrinkBound[headVertex].precedeOrderingRestriction = new List<int>();
                }
                else
                {
                    for (int i = 0; i < dynamicShrinkBound[j + 1].precedeOrderingRestriction.Count; i++)
                    {
                        if (dynamicShrinkBound[j + 1].precedeOrderingRestriction[i] == tailVertex)
                        {
                            dynamicShrinkBound[j + 1].precedeOrderingRestriction.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Sort strongly connected components 
        /// </summary>
        public void SortSCCs(UPMSPInstances instance, Parameters parameters)
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(x => x.SWPTOrder).ToList();

            StronglyConnectedComponent sourceStronglyConnectedComponent = new StronglyConnectedComponent();
            sourceStronglyConnectedComponent.bucket = buckets[0];
            sourceStronglyConnectedComponent.numOfArcs += adjListOfBucketArcs[sourceStronglyConnectedComponent.bucket].Count;
            stronglyConnectedComponents.Add(sourceStronglyConnectedComponent);

            for (int i = 1; i < dynamicShrinkBound.Count - 1; i++) 
            { 
                int currentIndex = dynamicShrinkBound[i].index;
                int currentBuckertIndex = parameters.numOfBucketOnOneVertex * (currentIndex - 1) + 1;
                for (int n = 0; n < parameters.numOfBucketOnOneVertex; n++) 
                {
                    StronglyConnectedComponent stronglyConnectedComponent = new StronglyConnectedComponent();
                    stronglyConnectedComponent.bucket = buckets[currentBuckertIndex+n];
                    stronglyConnectedComponent.numOfArcs += adjListOfBucketArcs[stronglyConnectedComponent.bucket].Count;
                    stronglyConnectedComponents.Add(stronglyConnectedComponent);
                }
            }

            StronglyConnectedComponent sinkStronglyConnectedComponent = new StronglyConnectedComponent();
            sinkStronglyConnectedComponent.bucket = buckets.Last();
            sinkStronglyConnectedComponent.numOfArcs += adjListOfBucketArcs[sinkStronglyConnectedComponent.bucket].Count;
            stronglyConnectedComponents.Add(sinkStronglyConnectedComponent);

            for (int i = 1; i <= stronglyConnectedComponents.Count; i++)
            {
                stronglyConnectedComponents[i - 1].topologyOrder = i;
                stronglyConnectedComponents[i - 1].bucket.topologyOrder = i;
            }
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(x => x.index).ToList();
        }
        public void SortSCCs_backward(UPMSPInstances instance, Parameters parameters)
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(x => x.SWPTOrder).ToList();

            StronglyConnectedComponent sinkStronglyConnectedComponent = new StronglyConnectedComponent();
            sinkStronglyConnectedComponent.bucket = buckets.Last();
            sinkStronglyConnectedComponent.numOfArcs += adjListOfBucketArcs[sinkStronglyConnectedComponent.bucket].Count;
            stronglyConnectedComponents.Add(sinkStronglyConnectedComponent);

            for (int i = 1; i < dynamicShrinkBound.Count - 1; i++)
            {
                int currentIndex = dynamicShrinkBound[i].index;
                int currentBuckertIndex = parameters.numOfBucketOnOneVertex * (currentIndex - 1) + 1;
                for (int n = 0; n < parameters.numOfBucketOnOneVertex; n++)
                {
                    StronglyConnectedComponent stronglyConnectedComponent = new StronglyConnectedComponent();
                    stronglyConnectedComponent.bucket = buckets[currentBuckertIndex + n];
                    stronglyConnectedComponent.numOfArcs += adjListOfBucketArcs[stronglyConnectedComponent.bucket].Count;
                    stronglyConnectedComponents.Add(stronglyConnectedComponent);
                }
            }

            StronglyConnectedComponent sourceStronglyConnectedComponent = new StronglyConnectedComponent();
            sourceStronglyConnectedComponent.bucket = buckets[0];
            sourceStronglyConnectedComponent.numOfArcs += adjListOfBucketArcs[sourceStronglyConnectedComponent.bucket].Count;
            stronglyConnectedComponents.Add(sourceStronglyConnectedComponent);

            for (int i = 1; i <= stronglyConnectedComponents.Count; i++)
            {
                stronglyConnectedComponents[i - 1].topologyOrder = i;
                stronglyConnectedComponents[i - 1].bucket.topologyOrder = i;
            }
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(x => x.index).ToList();
        }
        /// <summary>
        ///  Initial Ordering Restrictions
        /// </summary>
        /// <param name="jobSet"></param>
        /// <param name="instance"></param>
        public void InitialOrderingRestriction(UPMSPInstances instance, int machineID)
        {
            instance.upperBoundOnCompletionTimeOfMachine[machineID] = instance.CalculateUpperBoundOnCompletionTime();
            //instance.upperBoundOnCompletionTimeOfMachine[machineID]  = instance.ReadCompleteTimes(switcher.filePath_UpperBound)[machineID] + 0.01;

            dynamicShrinkBound = new List<DynamicShrinkBound>();

            DynamicShrinkBound sourceJob = new DynamicShrinkBound();
            sourceJob.index = 0;
            sourceJob.precedeOrderingRestriction = new List<int>();
            sourceJob.succeedOrderingRestriction = new List<int>();
            sourceJob.ratioProcessingTimeToWeight = 0;
            sourceJob.lowerBound = 0;
            sourceJob.upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
            sourceJob.maxPrecedeUpperBound = 0;
            sourceJob.maxPrecedeIndex = 0;
            sourceJob._isUpdated = false;
            dynamicShrinkBound.Add(sourceJob);

            for (int j = 0; j < instance.numJobs; j++)
            {
                DynamicShrinkBound job = new DynamicShrinkBound();
                job.index = j + 1;
                job.precedeOrderingRestriction = new List<int>();
                job.succeedOrderingRestriction = new List<int>();
                job.ratioProcessingTimeToWeight = instance.processingTimes[j, machineID] / instance.weights[j];
                job.lowerBound = 0;
                job.upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
                job.maxPrecedeUpperBound = 0;
                job.maxPrecedeIndex = 0;
                job._isUpdated = true;
                dynamicShrinkBound.Add(job);
            }

            DynamicShrinkBound sinkJob = new DynamicShrinkBound();
            sinkJob.index = instance.numJobs + 1;
            sinkJob.precedeOrderingRestriction = new List<int>();
            sinkJob.succeedOrderingRestriction = new List<int>();
            sinkJob.ratioProcessingTimeToWeight = double.MaxValue;
            sinkJob.lowerBound = 0;
            sinkJob.upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
            sinkJob.maxPrecedeUpperBound = 0;
            sinkJob.maxPrecedeIndex = 0;
            sinkJob._isUpdated = false;
            dynamicShrinkBound.Add(sinkJob);
        }
        /// <summary>
        /// Obtain job ordering restriction
        /// </summary>
        /// <param name="conf"></param>
        /// <returns></returns>
        public void ObtainForwardOrderingRestriction()
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(x => x.ratioProcessingTimeToWeight).ToList();
            for (int j = 0; j < dynamicShrinkBound.Count; j++)
            {
                dynamicShrinkBound[j].SWPTOrder = j;
            }

            for (int j = 0; j < dynamicShrinkBound.Count; j++)
            {
                for (int i = 0; i < dynamicShrinkBound.Count; i++)
                {
                    if (i < j)
                    {
                        dynamicShrinkBound[j].precedeOrderingRestriction.Add(dynamicShrinkBound[i].index);
                    }
                    if (i > j)
                    {
                        dynamicShrinkBound[j].succeedOrderingRestriction.Add(dynamicShrinkBound[i].index);
                    }
                }
            }
        }
        /// <summary>
        /// Obtain job ordering restriction
        /// </summary>
        /// <param name="conf"></param>
        /// <returns></returns>
        public void ObtainBackwardOrderingRestriction()
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderByDescending(x => x.ratioProcessingTimeToWeight).ToList();
            for (int j = 0; j < dynamicShrinkBound.Count; j++)
            {
                dynamicShrinkBound[j].SWPTOrder = j;
            }
            for (int j = 0; j < dynamicShrinkBound.Count; j++)
            {
                for (int i = 0; i < dynamicShrinkBound.Count; i++)
                {
                    if (i > j)
                    {
                        dynamicShrinkBound[j].precedeOrderingRestriction.Add(dynamicShrinkBound[i].index);
                    }
                    if (i < j)
                    {
                        dynamicShrinkBound[j].succeedOrderingRestriction.Add(dynamicShrinkBound[i].index);
                    }
                }
            }
            
        }

        /// <summary>
        /// Initial Upper Bound On Vertex 
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="boundOnVertex"></param>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="thresholdCoeff"></param>
        /// <returns></returns>
        public void InitialUpperBoundOnVertex(UPMSPInstances instance, Solution solution, int machineID)
        {
            for (int i = 0; i < dynamicShrinkBound.Count; i++)
            {
                DynamicShrinkBound jobOrderingRestriction = dynamicShrinkBound[i];
                jobOrderingRestriction.lowerBound = 0;
                double upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
                //double upperBound = 0;
                //for (int j = 0; j < instance.numJobs; j++)
                //{
                //    upperBound += instance.processingTimes[j, machineID];
                //}
                jobOrderingRestriction.upperBound = upperBound;
            }
        }

        /// <summary>
        /// Initial Compatibility Upper Bound On Vertex 
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="boundOnVertex"></param>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="thresholdCoeff"></param>
        /// <returns></returns>
        public void InitialDynamicShrinkBound(UPMSPInstances instance, Parameters parameters, Solution solution, int machineID, double thresholdCoeff)
        {
            for (int i = 0; i < dynamicShrinkBound.Count; i++)
            {
                DynamicShrinkBound jobOrderingRestriction = dynamicShrinkBound[i];
                jobOrderingRestriction.lowerBound = 0;
                double upperBound = 0;
                if (jobOrderingRestriction.index == 0 || jobOrderingRestriction.index == instance.numJobs + 1)
                {
                    upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
                }
                else
                {
                    for (int j = 0; j <= i; j++)
                    {
                        if (dynamicShrinkBound[j].index == 0 || dynamicShrinkBound[j].index == instance.numJobs + 1)
                        {
                            upperBound += 0;
                        }
                        else
                        {
                            upperBound += instance.processingTimes[dynamicShrinkBound[j].index - 1, machineID] + parameters.numOfBucketOnOneVertex / 4;
                            //upperBound += conf.processingTimes[boundOnVertex[j].index - 1, machineID];
                            if (upperBound > instance.upperBoundOnCompletionTimeOfMachine[machineID])
                            {
                                upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
                                break;
                            }
                        }
                    }
                }
                jobOrderingRestriction.upperBound = upperBound;
                Console.WriteLine(jobOrderingRestriction.upperBound);
            }
        }

        /// <summary>
        /// Caculate Compatibility UpperBound on Vertex
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="thresholdCoeff"></param>
        public void CaculateDynamicShrinkBound(UPMSPInstances instance, Parameters parameters, Solution solution, int machineID) 
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(x => x.ratioProcessingTimeToWeight).ToList();
            dynamicShrinkBound[0].maxPrecedeUpperBound = 0;
            dynamicShrinkBound[0].maxPrecedeIndex = 0;

            bool flag = false;
            for (int i = 1; i < dynamicShrinkBound.Count - 1; i++) 
            {
                if (dynamicShrinkBound[i]._isUpdated)
                {
                    flag = true;
                }

                if (flag) 
                {
                    dynamicShrinkBound[i].maxPrecedeUpperBound = 0;
                    dynamicShrinkBound[i].maxPrecedeIndex = 0;
                    for (int j = 0; j < i; j++)
                    {
                        if (dynamicShrinkBound[i].precedeOrderingRestriction.Contains(dynamicShrinkBound[j].index))
                        {
                            if (dynamicShrinkBound[j].maxPrecedeUpperBound >= dynamicShrinkBound[i].maxPrecedeUpperBound)
                            {
                                dynamicShrinkBound[i].maxPrecedeUpperBound = dynamicShrinkBound[j].maxPrecedeUpperBound;
                                dynamicShrinkBound[i].maxPrecedeIndex = dynamicShrinkBound[j].index;
                            }
                        }
                    }
                    dynamicShrinkBound[i].maxPrecedeUpperBound += instance.processingTimes[dynamicShrinkBound[i].index - 1, machineID] + parameters.numOfBucketOnOneVertex / 4;
                    if (dynamicShrinkBound[i].maxPrecedeUpperBound <= dynamicShrinkBound[i].upperBound)
                    {
                        dynamicShrinkBound[i].upperBound = dynamicShrinkBound[i].maxPrecedeUpperBound;
                    }
                    dynamicShrinkBound[i]._isUpdated = false;
                }
            }
        }

        public void CaculateBackwardDynamicShrinkBound(UPMSPInstances instance, Parameters parameters, Solution solution, int machineID) 
        {
            foreach (DynamicShrinkBound startTimeBound in dynamicShrinkBound) 
            { 
                double processingTime = 0;
                if (startTimeBound.index != 0 && startTimeBound.index != instance.numJobs +1) 
                {
                    processingTime = instance.processingTimes[startTimeBound.index-1, machineID];
                }
                startTimeBound.upperBound -= processingTime;
                startTimeBound.upperBound = Math.Ceiling(startTimeBound.upperBound);
                //startTimeBound.upperBound = (startTimeBound.upperBound);
            }
        }

        /// <summary>
        /// Update the status of the compatibility bound 
        /// </summary>
        /// <param name="nonImprovingArcs"></param>
        public void UpdateDynamicShrinkBoundStatus(List<int[]> nonImprovingArcs) 
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderBy(o => o.index).ToList();
            foreach (int[] arc in nonImprovingArcs)
            {
                if (dynamicShrinkBound[arc[1]].maxPrecedeIndex == arc[0])
                {
                    dynamicShrinkBound[arc[1]]._isUpdated = true;
                }
            }
           dynamicShrinkBound = dynamicShrinkBound.OrderBy(o => o.SWPTOrder).ToList();
        }

        /// <summary>
        /// Initial Lower Bound On Vertex
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="solution"></param>
        /// <param name="machineID"></param>
        /// <param name="thresholdCoeff"></param>
        public void InitialLowerBoundOnVertex(UPMSPInstances instance, Parameters parameters, Solution solution, int machineID)
        {
            dynamicShrinkBound = dynamicShrinkBound.OrderByDescending(x => x.ratioProcessingTimeToWeight).ToList();
            for (int i = 0; i < dynamicShrinkBound.Count; i++)
            {
                DynamicShrinkBound jobOrderingRestriction = dynamicShrinkBound[i];
                jobOrderingRestriction.lowerBound = 0;
                jobOrderingRestriction.upperBound = instance.upperBoundOnCompletionTimeOfMachine[machineID];
                //jobOrderingRestriction.upperBound = solution.makespan + 1;

                //double lowerBound = jobOrderingRestriction.upperBound;
                //if (jobOrderingRestriction.index == 0 || jobOrderingRestriction.index == instance.numJobs + 1)
                //{
                //    lowerBound = 0;
                //}
                //else
                //{
                //    for (int j = 1; j <= i; j++)
                //    {
                //        if (dynamicShrinkBound[j - 1].index == 0 || dynamicShrinkBound[j - 1].index == instance.numJobs + 1)
                //        {
                //            lowerBound -= 0;
                //        }
                //        else
                //        {
                //            lowerBound -= (instance.processingTimes[dynamicShrinkBound[j - 1].index - 1, machineID] + parameters.numOfBucketOnOneVertex / 4);
                //            if (lowerBound < 0)
                //            {
                //                lowerBound = 0;
                //                break;
                //            }
                //        }
                //    }
                //}
                //jobOrderingRestriction.lowerBound = lowerBound;
            }
        }
    }

    public class Bucket
    {
        public int vertex { get; set; }
        public double ub { get; set; }
        public double lb { get; set; }
        public int index { get; set; }
        public double stepsize { get; set; }
        public double minReducedCost { get; set; }
        public List<ForwardLabel> labelSet { get; set; }
        public List<BackwardLabel> backwardLabelSet { get; set; }
        public int topologyOrder { get; set; }

        public Bucket() { }
        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="bucket"></param>
        public Bucket(Bucket bucket)
        {
            this.vertex = bucket.vertex;
            this.ub = bucket.ub;
            this.lb = bucket.lb;
            this.index = bucket.index;
            this.stepsize = bucket.stepsize;
            this.minReducedCost = bucket.minReducedCost;
            this.labelSet = new List<ForwardLabel>();
            for (int i = 0; i < bucket.labelSet.Count; i++)
            {
                this.labelSet.Add(new ForwardLabel(bucket.labelSet[i]));
            }
            this.backwardLabelSet = new List<BackwardLabel>();
            for (int i = 0; i < bucket.backwardLabelSet.Count; i++) 
            {
                this.backwardLabelSet.Add(new BackwardLabel(bucket.backwardLabelSet[i]));
            }
            this.topologyOrder = bucket.topologyOrder;
        }
        /// <summary>
        ///Remove dominated label in tempBucket
        /// </summary>
        /// <param name="newLabel"></param>
        /// <param name="lmSRCs"></param>
        /// <returns></returns>
        public void RemoveDominatedLabelInBucket(ForwardLabel newLabel, List<double> dualsOfLmSRCs, string directions)
        {
            for (int m = 0; m < labelSet.Count; m++)
            {
                double sumDualsLmSRCS = 0;
                for (int o = 0; o < labelSet[m].lmSRCsState.Count; o++)
                {
                    if (newLabel.lmSRCsState[o] > labelSet[m].lmSRCsState[o])
                    {
                        sumDualsLmSRCS += dualsOfLmSRCs[o];
                    }
                }

                if (((newLabel.time < labelSet[m].time) && (newLabel.reducedCost - sumDualsLmSRCS <= labelSet[m].reducedCost)) || (newLabel.time <= labelSet[m].time) && (newLabel.reducedCost - sumDualsLmSRCS < labelSet[m].reducedCost))
                {
                    labelSet.RemoveAt(m);
                    --m;
                }
            }
        }

        public void RemoveDominatedBackwardLabelInBucket(BackwardLabel newLabel, List<double> dualsOfLmSRCs, string directions)
        {
            for (int m = 0; m < backwardLabelSet.Count; m++)
            {
                double sumDualsLmSRCS = 0;
                for (int o = 0; o < backwardLabelSet[m].lmSRCsState.Count; o++)
                {
                    if (newLabel.lmSRCsState[o] > backwardLabelSet[m].lmSRCsState[o])
                    {
                        sumDualsLmSRCS += dualsOfLmSRCs[o];
                    }
                }

                if (((newLabel.time > backwardLabelSet[m].time) &&(newLabel.cumulativeWeight <= backwardLabelSet[m].cumulativeWeight) && (newLabel.baseReducedCost - sumDualsLmSRCS <= backwardLabelSet[m].baseReducedCost))|| ((newLabel.time >= backwardLabelSet[m].time) && (newLabel.cumulativeWeight < backwardLabelSet[m].cumulativeWeight) && (newLabel.baseReducedCost - sumDualsLmSRCS <= backwardLabelSet[m].baseReducedCost))|| ((newLabel.time >= backwardLabelSet[m].time) && (newLabel.cumulativeWeight <= backwardLabelSet[m].cumulativeWeight) && (newLabel.baseReducedCost - sumDualsLmSRCS < backwardLabelSet[m].baseReducedCost)))
                {
                    backwardLabelSet.RemoveAt(m);
                    --m;
                }
            }
        }

        /// <summary>
        /// Print the bucket
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "vertex: " + vertex + " ";
            str += "index: " + index + " ";
            str += "ub: " + ub + " ";
            str += "lb: " + lb + " ";
            str += "stepsize: " + stepsize + " ";
            str += "minReducedCost: " + minReducedCost + " ";
            return str;
        }
    }
    public class StronglyConnectedComponent
    {
        public Bucket bucket { get; set; }
        public int numOfArcs { get; set; }
        public int topologyOrder { get; set; }
        public StronglyConnectedComponent()
        {
            bucket = new Bucket();
            numOfArcs = 0;
            topologyOrder = 0;
        }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="SCC"></param>
        /// <param name="numOfArcs"></param>
        /// <param name="TopologyOrder"></param>
        public StronglyConnectedComponent(Bucket SCC, int numOfArcs, int TopologyOrder)
        {
            this.bucket = SCC;
            this.numOfArcs = numOfArcs;
            this.topologyOrder = TopologyOrder;
        }
        /// <summary>
        /// Print the strongly connected component
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "vertex: " + bucket.vertex + " ";
            str += "index: " + bucket.index + " ";
            str += "num of arcs: " + numOfArcs + " ";
            str += "topologyOrder: " + topologyOrder + " ";
            return str;
        }
    }
    public class BucketArc
    {
        public Bucket headBucket { get; set; }
        public int[] arc { get; set; }
        public double resourceConsumption { get; set; }

        public BucketArc() { }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="headBucket"></param>
        /// <param name="arc"></param>
        /// <param name="resourceConsumption"></param>
        public BucketArc(Bucket headBucket, int[] arc, double resourceConsumption, List<Bucket> buckets)
        {
            this.headBucket = headBucket;
            this.arc = new int[arc.Length];
            Array.Copy(arc, this.arc, arc.Length);
            this.resourceConsumption = resourceConsumption;
        }
        /// <summary>
        /// Print bucket arc
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "vertex: " + headBucket.vertex + " ";
            str += "index: " + headBucket.index + " ";
            str += "lower bound: " + headBucket.lb + " ";
            str += "arc: ";
            for (int i = 0; i < arc.Length; i++)
            {
                str += arc[i] + " ";
            }
            str += "resource consumption: " + resourceConsumption + " ";
            return str;
        }
    }
    public class Arc
    {
        public int tail { get; set; }
        public int head { get; set; }
        public double reducedCost { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public Arc()
        {
            tail = 0;
            head = 0;
            reducedCost = 0;
        }
        /// <summary>
        /// Copy Constructor
        /// </summary>
        /// <param name="tail"></param>
        /// <param name="head"></param>
        /// <param name="reducedCost"></param>
        public Arc(int tail, int head, double reducedCost)
        {
            this.tail = tail;
            this.head = head;
            this.reducedCost = reducedCost;
        }
        /// <summary>
        /// Print arc
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "tail: " + tail + " ";
            str += "head: " + head + " ";
            str += "reducedCost: " + reducedCost + " ";
            return str;
        }
    }

    public class CandidateBranchVariables
    {
        // Two typesL: Qij and Xkij
        public string variableType { get; set; }
        public int[] branchVariables { get; set; }
        public double nonIntegerability { get; set; }
        public double pseudoCosts { get; set; }
        public List<Node> childNodes { get; set; }
        public CandidateBranchVariables()
        {
        }
        /// <summary>
        ///Copy Constructor
        /// </summary>
        /// <param name="candidateBranchVariables"></param>
        public CandidateBranchVariables(CandidateBranchVariables candidateBranchVariables)
        {
            this.variableType = candidateBranchVariables.variableType;
            this.branchVariables = new int[candidateBranchVariables.branchVariables.Length];
            Array.Copy(candidateBranchVariables.branchVariables, this.branchVariables, candidateBranchVariables.branchVariables.Length);
            this.nonIntegerability = candidateBranchVariables.nonIntegerability;
            this.pseudoCosts = candidateBranchVariables.pseudoCosts;
            this.childNodes = new List<Node>();
            for (int i = 0; i < candidateBranchVariables.childNodes.Count; i++)
            {
                this.childNodes.Add(new Node(candidateBranchVariables.childNodes[i]));
            }
        }
        /// <summary>
        /// Print candidate branch variables
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "variableType: " + variableType + " ";
            str += "branchVariables: ";
            for (int i = 0; i < branchVariables.Length; i++)
            {
                str += branchVariables[i] + " ";
            }
            str += "nonIntegerability: " + nonIntegerability + " ";
            str += "pseudoCosts: " + pseudoCosts + " ";
            str += "childNodes: ";
            for (int i = 0; i < childNodes.Count; i++)
            {
                str += childNodes[i].objValue.ToString() + " ";
            }
            return str;
        }
    }

    public class DynamicShrinkBound
    {
        public int index { get; set; }
        public List<int> precedeOrderingRestriction { get; set; }
        public List<int> succeedOrderingRestriction { get; set; }
        public double ratioProcessingTimeToWeight { get; set; }
        public int SWPTOrder { get; set; }
        public double upperBound { get; set; }
        public double lowerBound { get; set; }
        public double maxPrecedeUpperBound { get; set; }
        public int maxPrecedeIndex { get; set; }
        public bool _isUpdated { get; set; }

        public DynamicShrinkBound() { }
        /// <summary>
        /// Copy Constructor
        /// </summary>
        /// <param name="vertex"></param>
        public DynamicShrinkBound(DynamicShrinkBound vertex)
        {
            this.index = vertex.index;
            this.precedeOrderingRestriction = new List<int>();
            for (int i = 0; i < vertex.precedeOrderingRestriction.Count; i++)
            {
                this.precedeOrderingRestriction.Add(vertex.precedeOrderingRestriction[i]);
            }
            this.succeedOrderingRestriction = new List<int>();
            for (int i = 0; i < vertex.succeedOrderingRestriction.Count; i++)
            {
                this.succeedOrderingRestriction.Add(vertex.succeedOrderingRestriction[i]);
            }
            this.ratioProcessingTimeToWeight = vertex.ratioProcessingTimeToWeight;
            this.SWPTOrder = vertex.SWPTOrder;
            this.upperBound = vertex.upperBound;
            this.lowerBound = vertex.lowerBound;
            this.maxPrecedeUpperBound = vertex.maxPrecedeUpperBound;
            this.maxPrecedeIndex = vertex.maxPrecedeIndex;
            this._isUpdated = vertex._isUpdated;
        }
        /// <summary>
        /// Print job ordering restriction
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string str = "";
            str += "index: " + index + " ";
            str += "maxPrecedeUpperBound: " + maxPrecedeUpperBound + " ";
            str += "maxPrecedeIndex: " + maxPrecedeIndex + " ";
            str += "precedeOrderingRestriction: ";
            for (int i = 0; i < precedeOrderingRestriction.Count; i++)
            {
                str += precedeOrderingRestriction[i] + " ";
            }
            str += "succeedOrderingRestriction: ";
            for (int i = 0; i < succeedOrderingRestriction.Count; i++)
            {
                str += succeedOrderingRestriction[i] + " ";
            }
            str += "ratioProcessingTimeToWeight: " + ratioProcessingTimeToWeight + " ";
            str += "SWPT Order: " + SWPTOrder + " ";
            str += "upperBound: " + upperBound + " ";
            str += "lowerBound: " + lowerBound + " ";
            return str;
        }
    }
}
