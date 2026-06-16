from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = REPO_ROOT / "data"
RESULTS_DIR = REPO_ROOT / "results"

ALGORITHM_DIRS = {
    "BPC": "BPC",
    "No BG": "No Buck. Graph",
    "No DSub": "No DSUB",
    "No HP": "No Heur. Pric",
    "No LMSRC": "No Lm-SRCs",
    "No SB": "No Stro. Bran",
    "No VF": "No Vari. Fixi",
    "SE": "No Sche. Enum",
}


def get_data_root():
    return DATA_DIR


def get_results_root():
    return RESULTS_DIR


def get_raw_results_dir(label="BPC"):
    return get_results_root() / ALGORITHM_DIRS.get(label, label)


def get_summary_dir(name):
    return get_results_root() / name


def get_summary_results_file(label, filename="summary_results.xlsx"):
    return get_raw_results_dir(label) / filename
