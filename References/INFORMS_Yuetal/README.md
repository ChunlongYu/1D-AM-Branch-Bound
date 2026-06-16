[![INFORMS Journal on Computing Logo](https://INFORMSJoC.github.io/logos/INFORMS_Journal_on_Computing_Header.jpg)](https://pubsonline.informs.org/journal/ijoc)

# An Exact Branch-Price-and-Cut Algorithm for the Unrelated Parallel Machine Scheduling Problem

This archive is distributed in association with the [INFORMS Journal on Computing](https://pubsonline.informs.org/journal/ijoc) under the [MIT License](LICENSE).

The software and data in this repository are a snapshot of the software and data
that were used in the research reported on in the paper [An Exact Branch-Price-and-Cut Algorithm for the Unrelated Parallel Machine Scheduling Problem](https://doi.org/10.1287/ijoc.2024.0704) by Yang Yu, Xiaolong Li, Roberto Baldacci, Zhiqiao Wu, Wei Sun, Jiafu Tang, and Han Zhu.

**Important: This code is being developed on an on-going basis at https://github.com/Luckydragon96/UPMSP-TWCT-Instances.git. Please go there if you would like to
get a more recent version or would like support.**

## Cite

To cite the contents of this repository, please cite both the paper and this repository using their respective DOIs.

Paper DOI:

https://doi.org/10.1287/ijoc.2024.0704

Repository DOI:

https://doi.org/10.1287/ijoc.2024.0704.cd

Below is the BibTeX entry for citing this repository snapshot.

```bibtex
@misc{UPMSP2026,
  author =        {Yang Yu and Xiaolong Li and Roberto Baldacci and Zhiqiao Wu and Wei Sun and Jiafu Tang and Han Zhu},
  publisher =     {INFORMS Journal on Computing},
  title =         {An Exact Branch-Price-and-Cut Algorithm for the Unrelated Parallel Machine Scheduling Problem},
  year =          {2026},
  doi =           {10.1287/ijoc.2024.0704.cd},
  url =           {https://github.com/INFORMSJoC/2024.0704},
  note =          {Available for download at https://github.com/INFORMSJoC/2024.0704},
}
```

## Description

The goal of this software is to solve instances of the unrelated parallel machine scheduling problem using an exact branch-price-and-cut framework and to reproduce the computational results reported in the paper.

The repository currently contains the following main components.

- `UPMSP - Branch-Cut-and-Price Algorithm/`: the main executable project and driver program.
- `UPMSP - Branch-Cut-and-Price Algorithm.Core/`: the core solver project referenced by the executable.
- `UPMSP - Branch-Cut-and-Price Algorithm.Model/`: a supporting class library used by the executable.
- `data/`: benchmark instances used by the computational experiments.
- `results/`: raw and processed solver output, organized by algorithm variant.
- `scripts/`: Python post-processing scripts for summaries, unsolved-instance counting, and performance profiles.
- `Build.proj`: an MSBuild entry point for building the projects.
- `Directory.Build.props`: shared build logic, including validation of the local CPLEX path.
- `Directory.Build.local.props.example`: an example local configuration file for setting the CPLEX installation directory.

This archive contains source code, data, and scripts only. Compiled binaries, build directories, and third-party redistributable files such as CPLEX DLLs are intentionally excluded.

## Requirements

To build or run the code, the following software is required.

- Microsoft Windows.
- Microsoft .NET Framework 4.7.2.
- Microsoft Visual Studio 2022, or an equivalent MSBuild-compatible environment for .NET Framework projects.
- IBM ILOG CPLEX Optimization Studio 12.8.
- Python 3 with the packages `pandas`, `openpyxl`, `numpy`, and `matplotlib` for the post-processing scripts.

The code relies on external dependencies that are not redistributed in this archive. In particular, `ILOG.CPLEX.dll` and `ILOG.Concert.dll` must be available locally through the `CplexBinDir` property. This can be provided in one of two ways.

1. Set the environment variable `CPLEX_BIN_DIR`.
2. Create `Directory.Build.local.props` from `Directory.Build.local.props.example` and set `CplexBinDir` manually.

An example local configuration is shown below.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <CplexBinDir>C:\IBM\ILOG\CPLEX_Studio128\cplex\bin\x64_win64</CplexBinDir>
  </PropertyGroup>
</Project>
```

## Building

After configuring the local dependencies and the CPLEX path, the code can be built in Visual Studio or through the provided MSBuild entry point from the repository root.

```powershell
dotnet msbuild .\src\Build.proj /t:BuildAll /p:Configuration=Debug /p:Platform=x64
```

## Running

The main entry point is:

- `UPMSP - Branch-Cut-and-Price Algorithm/Program.cs`

The experiment configuration is controlled directly in the source code, primarily through:

- `UPMSP - Branch-Cut-and-Price Algorithm/Parameters.cs`
- `UPMSP - Branch-Cut-and-Price Algorithm/Switcher.cs`

The repository uses fixed top-level `data/` and `results/` directories under the workspace root.

- Input instances are read directly from `data/`.
- Raw and aggregated solver output is written under `results/`.
- The executable references the `UPMSP - Branch-Cut-and-Price Algorithm.Core/` project directly during build.

Before running the code, users should verify:

- the list of instances selected in `Parameters.cs`;
- the availability of the required instance files under `data/`;
- the local CPLEX installation and configuration.

## Replicating

To replicate the computational experiments reported in the paper, first configure the software environment described above, then build the code and run the main executable project.

The current repository layout supports the following workflow.

1. Build the solution with `Debug|x64` or another compatible configuration.
2. Run the executable project to generate solver output in `results/`.
3. For each algorithm variant to be analyzed, generate `summary_results.xlsx` with `scripts/Overall Summary by Index.py`.
4. For each algorithm variant to be analyzed, generate `average_results.xlsx` with `scripts/Overall Summary by Scale.py`.
5. Run `scripts/Prepare Summary Directories.py` to populate `results/Summary Results by Index/` and `results/Summary Results by Scale/` from the per-variant summary workbooks.
6. Run `scripts/Unsolved Instance Count.py` when an unsolved-instance list is needed.
7. Run the scripts under `scripts/Performance Profile/` to generate the performance-profile figures.

The benchmark instances currently used by the code are stored directly in `data/`. The raw solver output is stored by variant in directories such as:

- `results/BCP-DSUB/`
- `results/No Buck. Graph/`
- `results/No DSUB/`
- `results/No Lm-SRCs/`
- `results/No Stro. Bran/`
- `results/No Vari. Fixi/`
- `results/No Sche. Enum/`

## Ongoing Development

This code is being developed on an on-going basis at the author's
[Github site](https://github.com/Luckydragon96).

## Support

For questions or issues, please contact the authors (see [AUTHORS](AUTHORS)) or open an issue at the
[issue](https://github.com/Luckydragon96/UPMSP-TWCT-Instances/issues/new).
