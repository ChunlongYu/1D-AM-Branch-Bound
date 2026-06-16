import os

import pandas as pd

from path_utils import get_raw_results_dir


def process_local_excel():
    folder_path = get_raw_results_dir("BPC")
    input_path = os.path.join(folder_path, "summary_results.xlsx")
    output_path = os.path.join(folder_path, "average_results.xlsx")

    print(f"Reading file: {input_path}")

    try:
        df = pd.read_excel(input_path, engine="openpyxl")
    except FileNotFoundError:
        print(f"Error: file not found: {input_path}")
        return
    except Exception as exc:
        print(f"Error reading Excel file: {exc}")
        return

    if "Filename" not in df.columns:
        print("Error: missing 'Filename' column.")
        return

    def extract_m_n(filename):
        parts = str(filename).split("_")
        if len(parts) >= 2:
            return int(parts[0]), int(parts[1])
        return None, None

    df["m"], df["n"] = zip(*df["Filename"].apply(extract_m_n))
    result = df.groupby(["m", "n"])[["Objective Value", "Total Running Time"]].mean().reset_index()
    result = result.sort_values(by=["m", "n"])
    result.insert(0, "Group (m-n)", result["m"].astype(str) + "-" + result["n"].astype(str))
    result = result.rename(
        columns={
            "Objective Value": "Average Objective Value",
            "Total Running Time": "Average Total Running Time",
        }
    )
    final_df = result[["Group (m-n)", "Average Objective Value", "Average Total Running Time"]]
    final_df.to_excel(output_path, index=False)
    print(f"Saved averages to {output_path}")


if __name__ == "__main__":
    process_local_excel()
