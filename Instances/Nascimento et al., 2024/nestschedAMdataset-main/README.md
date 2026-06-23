# Nesting and scheduling in Additive Manufacturing
This repository contains instances and respective results for the Additive Manufacturing (AM) nesting and scheduling problem and was created based on the following works:
 - Nascimento, P. J., Silva, C., Antunes, C. H., & Moniz, S. (2024). *Optimal decomposition approach for solving large nesting and scheduling problems of additive manufacturing systems.* European Journal of Operational Research, 317(1), 92-110.
 - Nascimento, P. J., Silva, C., Antunes, C. H., Maravelias, C. T., & Moniz, S. (under review). *Improving the efficiency of Logic-based Benders Decomposition for production scheduling problems.*

## The AM nesting and scheduling problem
The AM nesting and scheduling problem consists of grouping a set of parts into batches and, subsequently, assigning and sequencing those batches in the machines available. This resembles a classical batch processing machine scheduling problem. The caveat is that the process of grouping parts into batches requires a 2D or 3D packing problem to guarantee that parts within a batch can be positioned in the building platform of the AM machine without overlapping. In this case, a 2D packing problem with irregular-shaped parts is considered, which is designated as nesting. To tackle nesting, the dotted-board model is used, where the building platform of the AM machines (i.e., the placement area) is represented by a mesh of dots that are evenly distributed and parts have a positioning point that is assigned to one of those dots.

## Data
Each folder contains the instances randomly generated for each number of parts. In the ```1.Polygons``` folder, the parts (polygons) created can be seen in ```allPolygons.txt```. The area of the parts (```area.txt```) can also be seen. In the ```2.PartsSpecs``` folder it is possible to see all the instances that were tested for the parts created. Each instance has information about the parts' release date, processing time, due date, and area. For more information on how these release dates, processing times, and due dates were calculated, see the original paper. The indices of the parts follow the order by which they appear in the respective files.

It is worth noting that the folder ```30Parts-Sorted``` contains the exact same parts/instances as the folder ```30Parts```, except for the fact that the index of the parts is sorted according to prioritization criteria defined in the paper Nascimento et al. (under review) (improved search procedures method). The folders ```80Parts-Sorted``` and ```120Parts-Sorted``` contain instances that follow the same sorting criteria.

## Results

### 1. Results from "Optimal Decomposition Approach for Solving Large Nesting and Scheduling Problems of Additive Manufacturing Systems"
These results are stored in the ```Results``` folder. Inside, you will find two subfolders, each containing results for:

- **COPCSP** Algorithm

- **MIPCSP** Algorithm

Files are named using the format ```nbParts-nbDots-nbMachines.xlsx``` where:

- ```nbParts```: Number of parts to schedule.

- ```nbDots```: Number of dots representing the building platform.

- ```nbMachines```: Number of machines considered.

Each file provides results for all instances corresponding to the specific combination of parameters.

### 2. Results from "Improving the Efficiency of Logic-based Benders Decomposition for Production Scheduling Problems"
These results are stored in the ```Results_Improvements``` folder. This folder contains four subfolders organized as follows:

- Results for **30 parts**, considering each individual acceleration method.

- Results for **30 parts**, considering multiple acceleration methods.

- Results for **80 parts**.

- Results for **120 parts**.

Files follow the naming convention ```x.x.x_DEC_MSP_MMP_MC_nbParts-nbDots-nbMachines.xlsx``` where:

- ```DEC```: Decomposition approach used.

- ```MSP```: Methods applied to the subproblems.
  
- ```MMP```: Methods applied to the master problem.
  
- ```MC```: Methods for deriving stronger cuts.

For detailed explanations of ```DEC```, ```MSP```, ```MMP```, and ```MC```, refer to the respective paper.
