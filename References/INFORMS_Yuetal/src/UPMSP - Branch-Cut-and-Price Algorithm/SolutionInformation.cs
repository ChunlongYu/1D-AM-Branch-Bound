using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ClosedXML.Excel;

namespace UPMSP_Branch_Cut_and_Price_Algorithm
{
    public  class SolutionInformation
    {
        public int numMachines { get; set; }
        public int numJobs { get; set; }
        public int run { get; set; }
        public double totalRunningTime { get; set; }
        public int numOfCGIterations { get; set; }
        public int numOfCCGIterations { get; set; }
        public int numOfRemovedArcs { get; set; }
        public int numOfExploreNodes { get; set; }
        public double separationTime { get; set; }
        public Solution bestSolution { get; set; }
        public double[,] initialDynamicShrinkBounds { get; set; }
        public double[,] currentDynamicShrinkBounds { get; set; }
        public double[] listSchedulingSolution { get; set; }
        public double[] columnGenerationSolution { get; set; }
        public double[] rowAndColumnGenerationSolution { get; set; }
        public double[] variableFixingSolution { get; set; }
        public double[] scheduleEnumerationSolution { get; set; }
        public double[] strongBranchingSolution { get; set; }
        public SolutionInformation() { }
        public SolutionInformation(int numMachines, int numJobs, int run)
        {
            this.numMachines = numMachines;
            this.numJobs = numJobs;
            this.run = run;
            this.totalRunningTime = 0;
            this.numOfCGIterations = 0;
            this.numOfCCGIterations = 0;
            this.numOfRemovedArcs = 0;
            this.bestSolution = new Solution();
            this.initialDynamicShrinkBounds = new double[numMachines, numJobs];
            this.currentDynamicShrinkBounds = new double[numMachines, numJobs];
            // 0 -- if optimal (0:No 1:Yes)   1 -- solution time  2 -- solution value  3  -- gap [%] 
            this.listSchedulingSolution = new double[4];
            //  0 -- if optimal (0:No 1:Yes)  1 -- solution time  2 -- solution value  3  -- gap [%]  4 -- number of columns  5 -- number of iterations
            this.columnGenerationSolution = new double[6];
            //  0 -- if optimal (0:No 1:Yes)  1 -- solution time  2 -- solution value  3  -- gap [%]  4 -- separation time        5 -- number of lm-SRCs      6 -- number of iterations
            this.rowAndColumnGenerationSolution = new double[7];
            //  0 -- if optimal (0:No 1:Yes)  1 -- number of removed arcs  2 -- solution time  3 -- number of affected dynamic shrink bounds   4 -- reduced dynamic shrink bounds
            this.variableFixingSolution = new double[5];
            // 0 -- if optimal (0:No 1:Yes)   1 -- solution time  2 -- solution value  3  -- number of columns
            this.scheduleEnumerationSolution = new double[4];
            // 0 -- if optimal (0:No 1:Yes)   1 -- solution time  2 -- solution value  3  -- number of nodes   
            this.strongBranchingSolution = new double[4];
        }

        /// <summary>
        /// Record the solution information of list scheduling
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="previousTime"></param>
        /// <param name="solution"></param>
        public void RecordListSchedulingSolutionInformation(DateTime currentTime, DateTime previousTime, Solution solution) 
        {
            listSchedulingSolution[0] = 0;
            listSchedulingSolution[1] = (currentTime - previousTime).TotalSeconds;
            listSchedulingSolution[2] = solution.objValue;
        }
        /// <summary>
        /// Record the solution information of column generation
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="previousTime"></param>
        /// <param name="solution"></param>
        public void RecordGenerationSolutionSolutionInformation(DateTime currentTime, DateTime previousTime, Solution solution)
        {
            if (solution._isInteger)
            {
                columnGenerationSolution[0] = 1;
            }
            columnGenerationSolution[1] = (currentTime - previousTime).TotalSeconds;
            columnGenerationSolution[2] = solution.objValue;
            for (int k = 0; k < numMachines; k++)
            {
                columnGenerationSolution[4] += solution.usedPartialScheduleSet[k].Count;
            }
            columnGenerationSolution[5] = numOfCGIterations;
        }
        /// <summary>
        ///  Record the solution information of row-and-column generation
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="previousTime"></param>
        /// <param name="solution"></param>
        public void RecordRowAndColumnGenerationSolutionInformation(DateTime currentTime, DateTime previousTime, Solution solution)
        {
            if (solution._isInteger)
            {
                rowAndColumnGenerationSolution[0] = 1;
            }
            rowAndColumnGenerationSolution[1] = (currentTime - previousTime).TotalSeconds;
            rowAndColumnGenerationSolution[2] = solution.objValue;
            rowAndColumnGenerationSolution[4] = separationTime;
            rowAndColumnGenerationSolution[5] = solution.usedLmSRCs.Count;
            rowAndColumnGenerationSolution[6] = numOfCCGIterations;
        }
        /// <summary>
        /// Record the solution information of variable fixing by reduced cost
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="previousTime"></param>
        /// <param name="solution"></param>
        public void RecordVariableFixingSolutionInformation(DateTime currentTime, DateTime previousTime, Solution solution)
        {
            variableFixingSolution[0] = 0;
            variableFixingSolution[1] = numOfRemovedArcs;
            variableFixingSolution[2] = (currentTime - previousTime).TotalSeconds;
        }
        /// <summary>
        /// Record the solution information of schedule enumeration
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="previousTime"></param>
        /// <param name="solution"></param>
        public void RecordScheduleEnumerationSolutionInformation(DateTime currentTime, DateTime previousTime, Solution solution)
        {
            if (solution._isInteger)
            {
                scheduleEnumerationSolution[0] = 1;
            }
            scheduleEnumerationSolution[1] = (currentTime - previousTime).TotalSeconds;
            scheduleEnumerationSolution[2] = solution.objValue;
            for (int k = 0; k < numMachines; k++)
            {
                scheduleEnumerationSolution[3] += solution.usedPartialScheduleSet[k].Count;
            }
            scheduleEnumerationSolution[3] = scheduleEnumerationSolution[3] - columnGenerationSolution[4];
        }
        /// <summary>
        ///  Record the solution information of strong branching
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="previousTime"></param>
        /// <param name="solution"></param>
        public void RecordStrongBranchingSolutionInformation(DateTime currentTime, DateTime previousTime, Solution solution)
        {
            strongBranchingSolution[0] = 1;
            strongBranchingSolution[1] = (currentTime - previousTime).TotalSeconds;
            strongBranchingSolution[2] = bestSolution.objValue;
            strongBranchingSolution[3] = numOfExploreNodes;
        }
        /// <summary>
        ///  Record dynamic shrink bounds
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public double[,] RecordDynamicShrinkBounds(Solution solution)
        {
            double[,] dynamicShrinkBounds = new double[numMachines, numJobs];
            for (int k = 0; k < numMachines; k++)
            {
                for (int j = 0; j < numJobs; j++)
                {
                    dynamicShrinkBounds[k, j] = solution.forwardBucketGraphs[k].dynamicShrinkBound[j + 1].upperBound;
                }
            }
            return dynamicShrinkBounds;
        }
        /// <summary>
        /// Calculate improvement rate
        /// </summary>
        public void CalculateImprovementRate() 
        {
            double[,] reducedDynamicShrinkBounds = new double[numMachines, numJobs];
            double totalImprovementRate = 0;
            for (int k = 0; k < numMachines; k++)
            {
                for (int j = 0; j <  numJobs; j++)
                {
                    reducedDynamicShrinkBounds[k, j] = (initialDynamicShrinkBounds[k, j] - currentDynamicShrinkBounds[k, j]) / initialDynamicShrinkBounds[k, j] * 100;
                    if (reducedDynamicShrinkBounds[k, j]!=0) 
                    {
                        variableFixingSolution[3] = variableFixingSolution[3] + 1;
                        totalImprovementRate += reducedDynamicShrinkBounds[k, j];
                    }
                }
            }
            if (variableFixingSolution[3]  == 0)
            {
                variableFixingSolution[4] = 0;
            }
            else 
            {
                variableFixingSolution[4] = totalImprovementRate / variableFixingSolution[3];
            }
            

            if (listSchedulingSolution[0] == 0)
            {
                listSchedulingSolution[3] = (listSchedulingSolution[2] - bestSolution.objValue) / bestSolution.objValue * 100;
            }
            if (columnGenerationSolution[0] == 0)
            {
                columnGenerationSolution[3] = (bestSolution.objValue - columnGenerationSolution[2]) / bestSolution.objValue * 100;

                if (rowAndColumnGenerationSolution[0] == 0)
                {
                    rowAndColumnGenerationSolution[3] = (bestSolution.objValue - rowAndColumnGenerationSolution[2]) / bestSolution.objValue * 100;
                }
            }
        }
        /// <summary>
        ///  Print solution information to Excel 
        /// </summary>
        /// <param name="solutionInfo"></param>
        public void PrintToExcel(SolutionInformation solutionInfo, string fileName)
        {
            using (var workbook = new XLWorkbook())
            {
                // Define the headers
                string[] listSchedulingSolutionHeaders = { "Optimal", "Solution Time", "Solution Value", "Gap [%]" };
                string[] columnGenerationSolutionHeaders = { "Optimal", "Solution Time", "Solution Value", "Gap [%]", "Number of Columns", "Number of Iterations" };
                string[] rowAndColumnGenerationSolutionHeaders = { "Optimal", "Solution Time", "Solution Value", "Gap [%]", "Separation Time", "Number of lm-SRCs", "Number of Iterations" };
                string[] variableFixingSolutionHeaders = { "Optimal", "Number of Removed Arcs", "Solution Time", "Number of Affected Dynamic Shrink Bounds", "Reduced Dynamic Shrink Bounds" };
                string[] scheduleEnumerationSolutionHeaders = { "Optimal", "Solution Time", "Solution Value", "Number of Columns" };
                string[] strongBranchingSolutionHeaders = { "Optimal", "Solution Time", "Solution Value", "Number of Nodes" };

                // Add the worksheet
                AddSolutionDetailsToWorkbook(workbook, solutionInfo);
                AddBestSchedulesToWorkbook(workbook, solutionInfo.bestSolution.bestSchedules);
                AddArrayToWorksheet(workbook, "Init. DSUB", solutionInfo.initialDynamicShrinkBounds, null);
                AddArrayToWorksheet(workbook, "Curr. DUSB", solutionInfo.currentDynamicShrinkBounds, null);
                AddArrayToWorksheet(workbook, "List Sche. Solution", solutionInfo.listSchedulingSolution, listSchedulingSolutionHeaders);
                AddArrayToWorksheet(workbook, "CG Solution", solutionInfo.columnGenerationSolution, columnGenerationSolutionHeaders);
                AddArrayToWorksheet(workbook, "CCG Solution", solutionInfo.rowAndColumnGenerationSolution, rowAndColumnGenerationSolutionHeaders);
                AddArrayToWorksheet(workbook, "Vari. Fixi. Solution", solutionInfo.variableFixingSolution, variableFixingSolutionHeaders);
                AddArrayToWorksheet(workbook, "Sche. Enum. Solution", solutionInfo.scheduleEnumerationSolution, scheduleEnumerationSolutionHeaders);
                AddArrayToWorksheet(workbook, "Stro. Bran. Solution", solutionInfo.strongBranchingSolution, strongBranchingSolutionHeaders);

                // Save the workbook
                Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                workbook.SaveAs(fileName);
            }
        }
        /// <summary>
        /// Add best schedules to workbook
        /// </summary>
        /// <param name="workbook"></param>
        /// <param name="bestSchedules"></param>
        private void AddBestSchedulesToWorkbook(XLWorkbook workbook, List<PartialSchedule> bestSchedules)
        {

             // Create and fill Processed Jobs table
            var worksheetJobs = workbook.Worksheets.Add("Processed Jobs");
            worksheetJobs.Cell(1, 1).Value = "Machine ID";
            worksheetJobs.Cell(1, 2).Value = "Processed Jobs";

            //  Create and fill Complete Times table
            var worksheetTimes = workbook.Worksheets.Add("Complete Times");
            worksheetTimes.Cell(1, 1).Value = "Machine ID";
            worksheetTimes.Cell(1, 2).Value = "Complete Time";

            int currentRow = 2;
            int machineIndex = 1;
            foreach (var schedule in bestSchedules)
            {
                // Add data to Processed Jobs table
                worksheetJobs.Cell(currentRow, 1).Value = $"Machine {machineIndex}";
                worksheetJobs.Cell(currentRow, 2).Value = string.Join(", ", schedule.setOfProcessedJobs);

                // Add data to Complete Times table
                worksheetTimes.Cell(currentRow, 1).Value = $"Machine {machineIndex}";
                worksheetTimes.Cell(currentRow, 2).Value = schedule.time;

                currentRow++;
                machineIndex++;
            }
        }
        /// <summary>
        ///  Add solution details to workbook
        /// </summary>
        /// <param name="workbook"></param>
        /// <param name="solutionInfo"></param>
        private  void AddSolutionDetailsToWorkbook(XLWorkbook workbook, SolutionInformation solutionInfo)
        {
            var worksheet = workbook.Worksheets.Add("Solution Details");

            // Add headers
            worksheet.Cell(1, 1).Value = "Property";
            worksheet.Cell(1, 2).Value = "Value";

           // Add data
            worksheet.Cell(2, 1).Value = "Objective Value";
            worksheet.Cell(2, 2).Value = solutionInfo.bestSolution.objValue;
            worksheet.Cell(3, 1).Value = "Total Running Time";
            worksheet.Cell(3, 2).Value = solutionInfo.totalRunningTime;
            worksheet.Cell(4, 1).Value = "Number of CG Iterations";
            worksheet.Cell(4, 2).Value = solutionInfo.numOfCGIterations;
            worksheet.Cell(5, 1).Value = "Number of CCG Iterations";
            worksheet.Cell(5, 2).Value = solutionInfo.numOfCCGIterations;
            worksheet.Cell(6, 1).Value = "Number of Explored Nodes";
            worksheet.Cell(6, 2).Value = solutionInfo.numOfExploreNodes;
            worksheet.Cell(7, 1).Value = "Number of Removed Arcs";
            worksheet.Cell(7, 2).Value = solutionInfo.numOfRemovedArcs;
        }
        /// <summary>
        ///  Add array to worksheet
        /// </summary>
        /// <param name="workbook"></param>
        /// <param name="sheetName"></param>
        /// <param name="array"></param>
        /// <param name="headers"></param>
        private void AddArrayToWorksheet(XLWorkbook workbook, string sheetName, double[,] array, string[] headers)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            if (headers != null)
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }
            }
            for (int i = 0; i < array.GetLength(0); i++)
            {
                for (int j = 0; j < array.GetLength(1); j++)
                {
                    worksheet.Cell(i + 2, j + 1).Value = array[i, j];
                }
            }
        }
        /// <summary>
        ///  Add array to worksheet
        /// </summary>
        /// <param name="workbook"></param>
        /// <param name="sheetName"></param>
        /// <param name="array"></param>
        /// <param name="headers"></param>
        private void AddArrayToWorksheet(XLWorkbook workbook, string sheetName, double[] array, string[] headers)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }
            for (int i = 0; i < array.Length; i++)
            {
                worksheet.Cell(2, i + 1).Value = array[i];
            }
        }
        /// <summary>
        ///  Excel data aggregatorain
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="switcher"></param>
        public void ExcelDataAggregatorain(Parameters parameters, Switcher switcher)
        {
            var sheetNames = new[] { "List Sche. Solution", "CG Solution", "CCG Solution", "Vari. Fixi. Solution", "Sche. Enum. Solution", "Stro. Bran. Solution"};
            var aggregatedData = new Dictionary<string, List<List<object>>>();
            Dictionary<string, string> headerRow = null;

            foreach (var sheetName in sheetNames)
            {
                aggregatedData[sheetName] = new List<List<object>>();
            }

            for (int index = 0; index < parameters.instanceList.Count; index++)
            {
                numMachines = parameters.instanceList[index][0];
                numJobs = parameters.instanceList[index][1];

                var summaryData = AggregateExcelData(numMachines, numJobs, sheetNames, out headerRow, parameters, switcher);
                foreach (var sheetName in sheetNames)
                {
                    var row = new List<object> { numMachines, numJobs };
                    row.AddRange(summaryData[sheetName].Select(kv => (object)kv.Value));
                    aggregatedData[sheetName].Add(row);
                }
            }

            CreateSummaryWorkbook(aggregatedData, headerRow, Path.Combine(switcher.resultsDirectory, "BCP-DSUB", "Solution Summary.xlsx"));
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\BCP-DSUB\\Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Heur. Pric.\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Sche. Enum.\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No DSUB\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Buck. Graph.\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Lm-SRCs\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Stro. Bran.\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Vari. Fixi.\\Solution Summary.xlsx");
            //CreateSummaryWorkbook(aggregatedData, headerRow, "UPMSP-TWCT Results\\No Domi. Rule\\Solution Summary.xlsx");
        }
        /// <summary>
        ///  Aggregate Excel data
        /// </summary>
        /// <param name="m"></param>
        /// <param name="n"></param>
        /// <param name="sheetNames"></param>
        /// <param name="runList"></param>
        /// <param name="switcher"></param>
        /// <param name="headerRow"></param>
        /// <returns></returns>
        private static Dictionary<string, Dictionary<string, double>> AggregateExcelData(int m, int n, string[] sheetNames, out Dictionary<string, string> headerRow, Parameters parameters, Switcher switcher)
        {
            var summaryData = new Dictionary<string, Dictionary<string, double>>();
            headerRow = new Dictionary<string, string>();
            var iValues = parameters.runList;

            foreach (var sheetName in sheetNames)
            {
                summaryData[sheetName] = new Dictionary<string, double>();

                foreach (var i in iValues)
                {
                    switcher.initializeFilesPath(m, n, i-1);
                    if (File.Exists(switcher.fileName))
                    {
                        using (var workbook = new XLWorkbook(switcher.fileName))
                        {
                            var worksheet = workbook.Worksheet(sheetName);
                            if (worksheet != null)
                            {
                                foreach (var cell in worksheet.RangeUsed().CellsUsed())
                                {
                                    // Header row
                                    if (cell.Address.RowNumber == 1) 
                                    {
                                        string key = $"{sheetName}_{cell.Address.ColumnNumber}";
                                        headerRow[key] = cell.GetString();
                                        continue;
                                    }

                                    string dataKey = $"{cell.Address.ColumnNumber}";
                                    if (!summaryData[sheetName].ContainsKey(dataKey))
                                    {
                                        summaryData[sheetName][dataKey] = 0;
                                    }

                                    if (cell.TryGetValue(out double value))
                                    {
                                        summaryData[sheetName][dataKey] += value / iValues.Count;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return summaryData;
        }
        /// <summary>
        ///  Create summary workbook
        /// </summary>
        /// <param name="data"></param>
        /// <param name="headerRow"></param>
        /// <param name="fileName"></param>
        private static void CreateSummaryWorkbook(Dictionary<string, List<List<object>>> data, Dictionary<string, string> headerRow, string fileName)
        {
            using (var summaryWorkbook = new XLWorkbook())
            {
                foreach (var sheetData in data)
                {
                    var summarySheet = summaryWorkbook.AddWorksheet(sheetData.Key);

                    // Add header row with 'm' and 'n' columns
                    summarySheet.Cell(1, 1).Value = "m";
                    summarySheet.Cell(1, 2).Value = "n";
                    int colIndex = 3;
                    foreach (var header in headerRow.Where(h => h.Key.StartsWith(sheetData.Key)))
                    {
                        summarySheet.Cell(1, colIndex).Value = header.Value;
                        colIndex++;
                    }

                    int rowIndex = 2;
                    foreach (var row in sheetData.Value)
                    {
                        for (int i = 0; i < row.Count; i++)
                        {
                            summarySheet.Cell(rowIndex, i + 1).Value = row[i];
                        }
                        rowIndex++;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                summaryWorkbook.SaveAs(fileName);
            }
        }
        /// <summary>
        ///  Solution details aggregatorain
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="switcher"></param>
        public void SolutionDetailsAggregatorain(Parameters parameters, Switcher switcher)
        {
            var aggregatedData = new List<(int m, int n, int i, object b2Value, object b3Value)>();
            var averageData = new List<(int m, int n, object avgB2, object avgB3)>();

            for (int index = 0; index < parameters.instanceList.Count; index++)
            {
                numMachines = parameters.instanceList[index][0];
                numJobs = parameters.instanceList[index][1];

                var b2Values = new List<double>();
                var b3Values = new List<double>();

                for (int i = 0; i < parameters.runList.Count; i++)
                {
                    int run = parameters.runList[i];
                    switcher.initializeFilesPath(numMachines, numJobs, run - 1);

                    if (File.Exists(switcher.fileName))
                    {
                        using (var workbook = new XLWorkbook(switcher.fileName))
                        {
                            var worksheet = workbook.Worksheet("Solution Details");
                            if (worksheet != null)
                            {
                                var b2Value = worksheet.Cell("B2").GetValue<double>();
                                var b3Value = worksheet.Cell("B3").GetValue<double>();
                                aggregatedData.Add((numMachines, numJobs, run, b2Value, b3Value));

                                b2Values.Add(b2Value);
                                b3Values.Add(b3Value);
                            }
                        }
                    }
                }

                if (b2Values.Any() && b3Values.Any())
                {
                    averageData.Add((numMachines, numJobs, b2Values.Average(), b3Values.Average()));
                }
            }

            // Creating a new workbook for the aggregated data
            using (var summaryWorkbook = new XLWorkbook())
            {
                var summarySheet = summaryWorkbook.AddWorksheet("Solution Details");
                summarySheet.Cell(1, 1).Value = "m";
                summarySheet.Cell(1, 2).Value = "n";
                summarySheet.Cell(1, 3).Value = "i";
                summarySheet.Cell(1, 4).Value = "Objective Value";
                summarySheet.Cell(1, 5).Value = "Total Running Time";

                int row = 2;
                foreach (var data in aggregatedData)
                {
                    summarySheet.Cell(row, 1).Value = data.m;
                    summarySheet.Cell(row, 2).Value = data.n;
                    summarySheet.Cell(row, 3).Value = data.i;
                    summarySheet.Cell(row, 4).Value = data.b2Value;
                    summarySheet.Cell(row, 5).Value = data.b3Value;
                    row++;
                }

                var averageSheet = summaryWorkbook.AddWorksheet("Average Solution Details");
                averageSheet.Cell(1, 1).Value = "m";
                averageSheet.Cell(1, 2).Value = "n";
                averageSheet.Cell(1, 3).Value = "Average Objective Value";
                averageSheet.Cell(1, 4).Value = "Average Total Running Time";

                row = 2;
                foreach (var data in averageData)
                {
                    averageSheet.Cell(row, 1).Value = data.m;
                    averageSheet.Cell(row, 2).Value = data.n;
                    averageSheet.Cell(row, 3).Value = data.avgB2;
                    averageSheet.Cell(row, 4).Value = data.avgB3;
                    row++;
                }
                
                string outputPath = Path.Combine(switcher.resultsDirectory, "Bulbul Sen Results", "Inst 0" + switcher.instanceGroup, "Solution Details.xlsx");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                summaryWorkbook.SaveAs(outputPath);
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\BCP-DSUB\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\BCP-DSUB\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Heur. Pric.\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Sche. Enum.\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No DSUB\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Buck. Graph.\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Lm-SRCs\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Stro. Bran.\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Vari. Fixi.\\Solution Details.xlsx");
                //summaryWorkbook.SaveAs("UPMSP-TWCT Results\\No Domi. Rule\\Solution Details.xlsx");
            }
        }
    }
}
