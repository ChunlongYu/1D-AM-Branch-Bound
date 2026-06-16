import shutil
from pathlib import Path

from path_utils import get_results_root, get_summary_dir


VARIANT_FILE_MAP = {
    "BCP-DSUB": ("summary_results_bpc.xlsx", "average_results_bpc.xlsx"),
    "No Buck. Graph": ("summary_results_no_bg.xlsx", "average_results_no_bg.xlsx"),
    "No DSUB": ("summary_results_no_dsub.xlsx", "average_results_no_dsub.xlsx"),
    "No Heur. Pric": ("summary_results_no_hp.xlsx", "average_results_no_hp.xlsx"),
    "No Lm-SRCs": ("summary_results_no_lmsrc.xlsx", "average_results_no_lmsrc.xlsx"),
    "No Stro. Bran": ("summary_results_no_sb.xlsx", "average_results_no_sb.xlsx"),
    "No Vari. Fixi": ("summary_results_no_vf.xlsx", "average_results_no_vf.xlsx"),
    "No Sche. Enum": ("summary_results_se.xlsx", "average_results_se.xlsx"),
}


def ensure_summary_directories():
    index_dir = get_summary_dir("Summary Results by Index")
    scale_dir = get_summary_dir("Summary Results by Scale")
    index_dir.mkdir(parents=True, exist_ok=True)
    scale_dir.mkdir(parents=True, exist_ok=True)
    return index_dir, scale_dir


def copy_if_exists(source_file: Path, target_file: Path):
    if not source_file.exists():
        return False
    shutil.copy2(source_file, target_file)
    return True


def main():
    results_root = get_results_root()
    index_dir, scale_dir = ensure_summary_directories()

    print(f"Results root: {results_root}")
    print(f"Index summary directory: {index_dir}")
    print(f"Scale summary directory: {scale_dir}")

    for variant, (index_name, scale_name) in VARIANT_FILE_MAP.items():
        variant_dir = results_root / variant
        summary_source = variant_dir / "summary_results.xlsx"
        average_source = variant_dir / "average_results.xlsx"

        copied_summary = copy_if_exists(summary_source, index_dir / index_name)
        copied_average = copy_if_exists(average_source, scale_dir / scale_name)

        print(
            f"{variant}: "
            f"summary={'copied' if copied_summary else 'missing'}; "
            f"average={'copied' if copied_average else 'missing'}"
        )


if __name__ == "__main__":
    main()
