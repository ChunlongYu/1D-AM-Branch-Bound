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
    public class Parameters
    {
        public List<int[]> instanceList { get; set; }
        public List<int> runList { get; set; }
        public int numOfBucketOnOneVertex { get; set; }
        public int numAddedColumns { get; set; }
        public int numOfSubsets { get; set; }
        public int numAddedLmSRCs { get; set; }
        public int maxIterationsLmSRCs { get; set; }
        public int numNeighbors { get; set; }
        public int sizeMemorySet { get; set; }
        public int maxIterationsNeighborhoodSearch { get; set; }
        public int maxNumSameObjValue { get; set; }
        public int numCandidatesInPhase0 { get; set; }
        public int numCandidatesInPhase1 { get; set; }
        public double maxGigaBytes { get; set; }
        public int maxNumEnumeratedLabels { get; set; }

        public Parameters()
        {
            this.instanceList = new List<int[]>() 
            {
                //new int[2] {4, 40}, new int[2] {4, 60}, new int[2] {4, 80},
                //new int[2] {8, 60}, new int[2] {8, 80}, new int[2] {8, 100},
                new int[2] {12, 60}, new int[2] {12, 80}, new int[2] {12, 100},
                new int[2] {16, 80}, new int[2] {16, 100},
                //new int[2] {16, 200},
                new int[2] {20, 80},new int[2] {20, 100},
                //new int[2] {20, 200},
            };

            this.runList = new List<int>() 
            {
                1, 2, 3,4, 5,
                6, 7, 8, 9, 10,
                11, 12, 13,14, 15,
                16, 17, 18, 19, 20
            };

            this.numOfBucketOnOneVertex = 4;
            //this.numOfBucketOnOneVertex = 1;

            // Row-and-Column Generation
            this.numAddedColumns = 8;
            this.numOfSubsets = 10000;
            this.numAddedLmSRCs = 10;
            this.maxIterationsLmSRCs = 30; 

            // Neighborhood Search
            this.numNeighbors = 10;
            this.sizeMemorySet = 20;
            this.maxIterationsNeighborhoodSearch = 50;
            this.maxNumSameObjValue = 5;

            // Strong Branching
            this.numCandidatesInPhase0 = 10;
            this.numCandidatesInPhase1 = 3;
            this.maxGigaBytes = 2;

            // Route Enumeration
            this.maxNumEnumeratedLabels = 10000;
        }
    }
}
