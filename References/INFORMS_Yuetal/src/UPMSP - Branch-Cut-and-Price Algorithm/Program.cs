using System;

namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    internal static class Program
    {
        static void Main()
        {
            // Input the number of machines and jobs
            int numMachines;
            int numJobs;

            // Select the algorithmic components
            Switcher switcher = new Switcher();
            switcher.processingTimeType = "Integer";
            //switcher.instanceType = "Write";
            switcher.dualPriceSmoothing = true;
            switcher.neighborhoodSearch = true;
            switcher.rowAndColumnGeneration = true;
            switcher.variableFixing = true;
            switcher.dynamicShrinkBound = true;
            switcher.strongBranching = true;
            //switcher.routeEnumeration = true;
            //switcher.branchVariable = "Xkij";

            // Initialize algorithm parameters
            Parameters parameters = new Parameters();

            for (int index = 0; index < parameters.instanceList.Count; index++)
            {
                numMachines = parameters.instanceList[index][0];
                numJobs = parameters.instanceList[index][1];

                for (int i = 0; i < parameters.runList.Count; i++)
                {
                    int run = parameters.runList[i] - 1;

                    // Initialize the file path
                    switcher.initializeFilesPath(numMachines, numJobs, run);

                    // Initialize the solution information
                    SolutionInformation solutionInfo = new SolutionInformation(numMachines, numJobs, run);

                    // Initialize the problem instance
                    UPMSPInstances instance = new UPMSPInstances(numMachines, numJobs, switcher, run);
                    // instance.PrintInstanceToExcel(switcher, run);

                    // Perform the Branch-Cut-and-Price algorithm
                    BranchCutAndPrice branchCutAndPrice = new BranchCutAndPrice(instance, parameters, switcher, solutionInfo);

                    // Print the solution information
                    // solutionInfo.PrintToExcel(solutionInfo, switcher.fileName);
                }
            }
        }
    }
}
