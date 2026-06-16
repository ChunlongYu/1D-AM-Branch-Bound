import os

from path_utils import get_raw_results_dir


mn_pairs = [
    (4, 40), (4, 60), (4, 80),
    (8, 60), (8, 80), (8, 100),
    (12, 60), (12, 80), (12, 100),
    (16, 80), (16, 100), (16, 200),
    (20, 80), (20, 100), (20, 200),
]
r_values = range(1, 21)

file_directory = get_raw_results_dir("BPC")
output_file = os.path.join(file_directory, "unsolved instances.txt")


def main():
    failed_experiments = []
    for m, n in mn_pairs:
        for r in r_values:
            file_name = f"{m}_{n}_solution information_{r}.xlsx"
            file_path = os.path.join(file_directory, file_name)
            if not os.path.exists(file_path):
                failed_experiments.append((m, n, r))

    with open(output_file, "w", encoding="utf-8") as handle:
        for m, n, r in failed_experiments:
            handle.write(f"m={m}, n={n}, r={r}\n")

    print(f"Saved unsolved instance list to {output_file}")


if __name__ == "__main__":
    main()
