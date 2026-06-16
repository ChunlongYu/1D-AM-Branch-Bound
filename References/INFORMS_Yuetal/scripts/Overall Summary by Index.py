import os
import re

import pandas as pd

from path_utils import get_raw_results_dir


def get_short_name(filename):
    match = re.search(r"(\d+)_(\d+)_solution information_(\d+)", filename)
    if match:
        return f"{match.group(1)}_{match.group(2)}_{match.group(3)}"
    return os.path.splitext(filename)[0]


def process_excel_files(folder_path, output_file="summary_results.xlsx"):
    summary_list = []

    if not os.path.exists(folder_path):
        print(f"Error: folder does not exist: {folder_path}")
        return

    print(f"Processing folder: {folder_path}")

    for filename in os.listdir(folder_path):
        if not (filename.endswith(".xlsx") or filename.endswith(".xls")):
            continue
        if filename.startswith("~$") or filename == output_file:
            continue

        file_path = os.path.join(folder_path, filename)
        short_name = get_short_name(filename)
        file_data = {"Filename": short_name}

        try:
            df_details = pd.read_excel(file_path, sheet_name="Solution Details")
            obj_val = df_details.loc[df_details["Property"] == "Objective Value", "Value"]
            total_time = df_details.loc[df_details["Property"] == "Total Running Time", "Value"]
            file_data["Objective Value"] = obj_val.values[0] if not obj_val.empty else None
            file_data["Total Running Time"] = total_time.values[0] if not total_time.empty else None
        except ValueError:
            print(f"Warning: missing 'Solution Details' in {filename}")
        except KeyError:
            pass
        except Exception as exc:
            print(f"Error processing {filename}: {exc}")
            continue

        summary_list.append(file_data)

    if not summary_list:
        print("No data collected.")
        return

    result_df = pd.DataFrame(summary_list)
    try:
        result_df["sort_key"] = result_df["Filename"].apply(
            lambda x: [int(s) if s.isdigit() else s for s in re.split(r"(\d+)", x)]
        )
        result_df = result_df.sort_values("sort_key").drop(columns=["sort_key"])
    except Exception:
        pass

    result_df = result_df[["Filename", "Objective Value", "Total Running Time"]]
    output_path = os.path.join(folder_path, output_file)
    result_df.to_excel(output_path, index=False)
    print(f"Saved summary to {output_path}")


if __name__ == "__main__":
    process_excel_files(get_raw_results_dir("BPC"))
