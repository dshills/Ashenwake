#!/usr/bin/env python3
"""Compare complete, matching campaign measurements; print JSON, never mutate inputs."""
import argparse
import json
from pathlib import Path


def read(path):
    return json.loads(path.read_text())


def load(directory):
    summary = read(directory / "summary.json")
    policy = read(directory / "policy.json")
    if summary.get("kind") != "CampaignBalanceMeasured" or summary["failures"]:
        raise ValueError(f"{directory}: campaign measurement did not complete successfully")
    if summary["policyHash"] != policy["policyHash"]:
        raise ValueError(f"{directory}: policy and summary identities differ")
    runs = {}
    for entry in summary["runs"]:
        report = read(directory / entry["report"])
        key = (report["discipline"], report["seed"])
        if key in runs or not report["complete"] or report["failure"]:
            raise ValueError(f"{directory}: duplicate or incomplete run {key}")
        if report["policyHash"] != summary["policyHash"]:
            raise ValueError(f"{directory}: run policy differs from summary {key}")
        if key != (entry["discipline"], entry["seed"]):
            raise ValueError(f"{directory}: report does not match summary {key}")
        runs[key] = report
    expected = {(discipline, 42 + seed) for discipline in
                ("Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden")
                for seed in range(summary["seeds"])}
    if set(runs) != expected:
        raise ValueError(f"{directory}: expected all five disciplines and declared seeds")
    return summary, policy, runs


def pair(before, after):
    return {"before": before, "after": after, "delta": after - before}


def compare(before_directory, after_directory):
    before, before_policy, before_runs = load(before_directory)
    after, after_policy, after_runs = load(after_directory)
    if (before["policyHash"], before["mainPath"], set(before_runs)) != (
            after["policyHash"], after["mainPath"], set(after_runs)):
        raise ValueError("Cannot compare different policies, routes, disciplines or seeds")
    results = []
    for key in sorted(before_runs):
        left, right = before_runs[key], after_runs[key]
        left_rooms = {(r["act"], r["roomId"]): r for r in left["rooms"]}
        right_rooms = {(r["act"], r["roomId"]): r for r in right["rooms"]}
        if set(left_rooms) != set(right_rooms):
            raise ValueError(f"Room coverage changed for {key}; compare those routes separately")
        def totals(report):
            return {metric: sum(r[metric] for r in report["rooms"]) for metric in
                    ("combatTicks", "travelTicks", "lootTicks", "damageTaken", "potions")}
        left_total, right_total = totals(left), totals(right)
        results.append({
            "discipline": key[0], "seed": key[1],
            "totals": {metric: pair(left_total[metric], right_total[metric]) for metric in left_total},
            "deaths": pair(left["deaths"], right["deaths"]),
            "finalLevel": pair(left["finalProgression"]["level"], right["finalProgression"]["level"]),
            "finalExperience": pair(left["finalProgression"]["experience"], right["finalProgression"]["experience"]),
            "finalMaterials": pair(left["finalProgression"]["materials"], right["finalProgression"]["materials"]),
            "rooms": [{"act": room[0], "roomId": room[1], **{
                metric: pair(left_rooms[room][metric], right_rooms[room][metric]) for metric in
                ("combatTicks", "damageTaken", "deaths", "entryLevel", "exitLevel", "experienceEarned")}}
                for room in sorted(left_rooms)]
        })
    return {"kind": "CampaignBalanceComparison", "policyHash": before["policyHash"],
            "policy": before_policy["description"], "mainPath": before["mainPath"],
            "beforeIdentities": before["identities"], "afterIdentities": after["identities"],
            "limits": after_policy["limits"], "units": after_policy["units"], "runs": results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(compare(args.before, args.after), indent=2))
    except (ValueError, KeyError, OSError) as error:
        parser.exit(1, f"Campaign comparison failed: {error}\n")
