using System;
using System.Diagnostics;
using System.IO;

namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    public class Switcher
    {
        public string exactDirection { get; set; }
        public bool dualPriceSmoothing { get; set; }
        public bool neighborhoodSearch { get; set; }
        public bool bidirectionalLabeling { get; set; }
        public bool rowAndColumnGeneration { get; set; }
        public bool routeEnumeration { get; set; }
        public bool variableFixing { get; set; }
        public bool dynamicShrinkBound { get; set; }
        public bool strongBranching { get; set; }
        public string branchVariable { get; set; }
        public string instanceType { get; set; }
        public string processingTimeType { get; set; }
        public string fileName { get; set; }
        public string filePath_UpperBound { get; set; }
        public string filePath_Instance { get; set; }
        public int instanceGroup { get; set; }
        public string dataDirectory { get; }
        public string resultsDirectory { get; }

        public Switcher()
        {
            dataDirectory = ResolveDataDirectory();
            resultsDirectory = ResolveRepositoryDirectory("results");

            if (!Directory.Exists(dataDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Input data directory was not found: '{dataDirectory}'. " +
                    "Place the input files under the repository data folder.");
            }

            Directory.CreateDirectory(resultsDirectory);

            // Forward Backward BiDirectional
            this.exactDirection = "Forward";
            this.dualPriceSmoothing = true;
            this.neighborhoodSearch = true;
            this.bidirectionalLabeling = false;
            this.rowAndColumnGeneration = true;
            this.routeEnumeration = false;
            this.variableFixing = true;
            this.dynamicShrinkBound = true;
            this.strongBranching = true;
            this.branchVariable = "Xkij";
            this.instanceType = "Read";
            this.processingTimeType = "Integer";

            fileName = Path.Combine(resultsDirectory, "Pro Max");
            filePath_UpperBound = Path.Combine(resultsDirectory, "Pro Max");

            this.filePath_Instance = dataDirectory;
        }

        public void initializeFilesPath(int numMachines, int numJobs, int run)
        {
            fileName = Path.Combine(resultsDirectory, "Pro Max");
            filePath_UpperBound = Path.Combine(resultsDirectory, "Pro Max");

            filePath_Instance = dataDirectory;

            string resultFileName = numMachines + "_" + numJobs + "_solution information_" + (run + 1) + ".xlsx";
            fileName = Path.Combine(fileName, resultFileName);
            filePath_UpperBound = Path.Combine(filePath_UpperBound, resultFileName);
            filePath_Instance = Path.Combine(filePath_Instance, numMachines + "_" + numJobs + "_TWCT_" + (run + 1) + ".xlsx");
            //filePath_Instance += "XH_" + numMachines + "_" + numJobs + "_TWCT_" + (run + 1) + ".xlsx";
        }

        private static string ResolveDataDirectory()
        {
            return ResolveRepositoryDirectory("data");
        }

        private static string ResolveRepositoryDirectory(string directoryName)
        {
            return Path.Combine(ResolveRepositoryRoot(), directoryName);
        }

        private static string ResolveRepositoryRoot()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;

            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "Directory.Build.props")))
                {
                    DirectoryInfo rootCandidate = Directory.GetParent(directory);
                    if (rootCandidate != null &&
                        (File.Exists(Path.Combine(rootCandidate.FullName, "README.md")) ||
                         Directory.Exists(Path.Combine(rootCandidate.FullName, "data")) ||
                         Directory.Exists(Path.Combine(rootCandidate.FullName, "results"))))
                    {
                        return rootCandidate.FullName;
                    }

                    return directory;
                }

                DirectoryInfo parent = Directory.GetParent(directory);
                directory = parent?.FullName;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
