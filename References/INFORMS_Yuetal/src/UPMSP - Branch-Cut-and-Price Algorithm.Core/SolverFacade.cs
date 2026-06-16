namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    public sealed class SolverFacade
    {
        public Solution Solve(UPMSPInstances instance, Parameters parameters, Switcher switcher, SolutionInformation solutionInfo)
        {
            new BranchCutAndPrice(instance, parameters, switcher, solutionInfo);
            return solutionInfo.bestSolution;
        }
    }
}
