#!/usr/bin/env python3
"""Validate and summarize disjoint earned tier 1–3 measurement batches; never merge saves."""

import argparse
import json
from pathlib import Path
import statistics


DISCIPLINES = ("Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden")
EXPECTED = {(discipline, seed) for discipline in DISCIPLINES for seed in (42, 43, 44)}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text())


def summarize(directories):
    reports = {}
    identity = None
    policy = None
    for directory in directories:
        declaration = read(directory / "policy.json")
        current = (declaration["policyHash"], declaration["identities"])
        if identity is None:
            policy, identity = current
        require(current == (policy, identity), f"Policy/content identities differ: {directory}")
        for path in sorted(directory.glob("*/report.json")):
            report = read(path)
            pair = (report["discipline"], report["seed"])
            require(pair in EXPECTED, f"Unexpected discipline/seed: {pair}")
            require(pair not in reports, f"Duplicate discipline/seed: {pair}")
            require((report["policyHash"], report["identities"]) == current, f"Report identity differs: {path}")
            require(report["commands"] == report["campaign"]["commands"] + report["endgame"]["commands"], f"Command totals differ: {path}")
            cursor = 0
            for segment in report["segments"]:
                through = segment["throughCommand"]
                require(segment["fromCommand"] == cursor and 0 < through - cursor <= 900, f"Replay gap/overlap/bound: {path}")
                replay = read(path.parent / segment["replay"])
                require(len(replay["frames"]) == through - cursor, f"Replay frame count differs: {path}")
                require(replay["frames"][-1]["stateHash"] == segment["finalHash"], f"Replay terminal hash differs: {path}")
                cursor = through
            require(cursor == report["commands"], f"Replay coverage incomplete: {path}")
            require(report["segments"][-1]["finalHash"] == report["stateHash"], f"Final segment hash differs: {path}")
            require(read(path.parent / "final.save.json")["stateHash"] == report["stateHash"], f"Final save hash differs: {path}")
            if report["handoff"] is not None:
                require(read(path.parent / "campaign-earned.save.json")["stateHash"] == report["handoff"]["stateHash"], f"Handoff save hash differs: {path}")
            rows = report["endgame"]["rooms"] or []
            duration_keys = ("combatTicks", "lootTicks", "travelTicks", "menuCommands", "rejectedOperations")
            require(sum(sum(row[key] for key in duration_keys) for row in rows) == report["endgame"]["commands"], f"Room command accounting differs: {path}")
            require(sum(row["deaths"] for row in rows) == report["endgameDeaths"], f"Death accounting differs: {path}")
            if report["handoff"] is not None:
                handoff = report["handoff"]
                final = report["finalProgression"]
                require(sum(row["experienceEarned"] for row in rows) == final["experience"] - handoff["experience"], f"Cumulative XP accounting differs across level-ups: {path}")
                materials_earned = sum(row["materialsEarned"] for row in rows)
                materials_spent = sum(row["materialsSpent"] for row in rows)
                require(materials_earned - materials_spent == final["materials"] - handoff["materials"], f"Material delta accounting differs: {path}")
                require(materials_spent == 0, f"Declared no-spending policy spent materials: {path}")
            if report["complete"]:
                require(report["failure"] == "" and report["tiersVerified"], f"Completion contradicts failure/receipts: {path}")
                require([entry["manifest"]["tier"] for entry in report["endgame"]["entries"]] == [1, 2, 3], f"Tier route differs: {path}")
                require(sorted(reward["tier"] for reward in report["endgame"]["rewards"]) == [1, 2, 3], f"Reward coverage differs: {path}")
                require(sum(row["rewardCommitted"] for row in rows) == len(report["endgame"]["completedRooms"]) == 12, f"Room receipt coverage differs: {path}")
            reports[pair] = (path, report)
    require(set(reports) == EXPECTED, f"Missing discipline/seed pairs: {sorted(EXPECTED - set(reports))}")
    values = [report for _, report in reports.values()]
    tiers = []
    for tier in (1, 2, 3):
        totals = []
        for pair, (path, report) in sorted(reports.items()):
            rows = [row for row in report["endgame"]["rooms"] or [] if not row["entry"]["inHub"] and row["entry"]["tier"] == tier]
            totals.append({"discipline": pair[0], "seed": pair[1],
                           **{key: sum(row[key] for row in rows) for key in ("combatTicks", "damageTaken", "potions", "deaths", "experienceEarned", "materialsEarned")},
                           "rooms": len(rows), "entryLevel": rows[0]["entry"]["level"] if rows else None,
                           "exitLevel": rows[-1]["exit"]["level"] if rows else None})
        ticks = [row["combatTicks"] for row in totals]
        damage = [row["damageTaken"] for row in totals]
        tiers.append({"tier": tier, "runs": totals,
                      "meanCombatTicks": statistics.mean(ticks), "minCombatTicks": min(ticks), "maxCombatTicks": max(ticks),
                      "meanDamageTaken": statistics.mean(damage), "minDamageTaken": min(damage), "maxDamageTaken": max(damage),
                      "deaths": sum(row["deaths"] for row in totals), "potions": sum(row["potions"] for row in totals)})
    return {"kind": "EarnedEndgameBatchSummary", "policyHash": policy, "identities": identity,
            "runs": len(values), "commands": sum(r["commands"] for r in values),
            "verifiedSegments": sum(len(r["segments"]) for r in values),
            "campaignDeaths": sum(r["campaignDeaths"] for r in values), "endgameDeaths": sum(r["endgameDeaths"] for r in values),
            "failures": sum(not r["complete"] for r in values),
            "sourceReports": [str(path) for path, _ in reports.values()], "tiers": tiers,
            "verification": "Exactly 15 unique pairs; matching declared and per-run policy/content identities; contiguous persisted replay intervals/frame counts/terminal hashes; exact handoff/final save header hashes; complete tier/room reward coverage; cumulative XP and net material deltas reconcile across level-ups, with no material spending. Runtime generation already executed and hash-verified each replay and loaded each save."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("directories", nargs="+", type=Path)
    args = parser.parse_args()
    require(not args.output.exists(), "Summary destination already exists; previous measurements are never overwritten.")
    result = summarize(args.directories)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x") as destination:
        json.dump(result, destination, indent=2)
        destination.write("\n")
    print(json.dumps({key: result[key] for key in ("kind", "runs", "commands", "verifiedSegments", "campaignDeaths", "endgameDeaths", "failures")}))
    return 1 if result["failures"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
