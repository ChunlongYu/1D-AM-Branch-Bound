import os
import sys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib import rcParams
from matplotlib.ticker import FuncFormatter

sys.path.append(str(Path(__file__).resolve().parents[1]))
from path_utils import get_results_root, get_summary_results_file


base_path = get_results_root()
folders = ["BPC", "No BG", "No DSub", "No LMSRC", "No SB", "No VF", "SE"]
m_values = [16, 20]
n_values = [80, 100, 200]
i_values = list(range(1, 21))

rcParams["font.family"] = "serif"
rcParams["font.serif"] = "Times New Roman"

data = {}
for folder in folders:
    file_path = get_summary_results_file(folder)
    if not os.path.exists(file_path):
        print(f"Warning: missing file: {file_path}")
        continue

    df = pd.read_excel(file_path)
    if "Filename" not in df.columns:
        print(f"Warning: missing Filename column in {file_path}")
        continue

    split_names = df["Filename"].str.split("_", expand=True)
    df["m"] = split_names[0].astype(int)
    df["n"] = split_names[1].astype(int)
    df["i"] = split_names[2].astype(int)
    df["Instance"] = df["m"].astype(str) + "-" + df["n"].astype(str) + "-" + df["i"].astype(str)
    df_filtered = df[df["m"].isin(m_values) & df["n"].isin(n_values) & df["i"].isin(i_values)]
    df_filtered = df_filtered[df_filtered["Total Running Time"].notnull() & (df_filtered["Total Running Time"] <= 3600)]
    if not df_filtered.empty:
        data[folder] = df_filtered.set_index("Instance")["Total Running Time"]

if not data:
    print("Error: no valid performance-profile input data found.")
else:
    all_instances = sorted(set().union(*[series.index for series in data.values()]))
    performance_data = pd.DataFrame(index=all_instances, columns=folders)
    for folder, series in data.items():
        performance_data[folder] = series
    performance_data.replace({None: np.nan, np.inf: np.nan}, inplace=True)
    normalized_data = performance_data.div(performance_data.min(axis=1), axis=0)

    max_tau = 5
    taus = np.linspace(1, max_tau, 500)
    profiles = pd.DataFrame(index=taus, columns=folders)

    for folder in folders:
        if folder not in normalized_data.columns:
            continue
        profiles[folder] = [(normalized_data[folder] <= tau).sum() / len(all_instances) for tau in taus]

    plt.figure(figsize=(10, 6))
    for folder in folders:
        if folder in profiles.columns:
            plt.plot(profiles.index, profiles[folder] * 100, label=folder)

    plt.xlabel("Performance metric", fontsize=12)
    plt.ylabel("Instances (%)", fontsize=12)
    plt.title("Performance Profiles", fontsize=14)
    plt.legend(fontsize=10)
    plt.grid(True, which="both", linestyle="--", alpha=0.5)
    plt.xlim(1, max_tau)
    plt.ylim(0, 105)
    plt.gca().yaxis.set_major_formatter(FuncFormatter(lambda y, _: f"{y:.0f}"))

    pdf_file_path = os.path.join(base_path, "updated performance profile large instances.pdf")
    plt.savefig(pdf_file_path, format="pdf", bbox_inches="tight")
    print(f"Saved plot to {pdf_file_path}")
    plt.show()
