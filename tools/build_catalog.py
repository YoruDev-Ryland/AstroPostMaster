#!/usr/bin/env python3
"""Regenerates src/AstroPostMaster.Core/Catalog/catalog.json.

Sources: OpenNGC (https://github.com/mattiaverga/OpenNGC, CC-BY-SA-4.0) pinned to a release tag,
plus tools/catalog_curated.json for popular names and non-NGC/IC objects OpenNGC lacks.
Usage: python3 tools/build_catalog.py
"""
import csv
import io
import json
import re
import urllib.request
from pathlib import Path

OPENNGC_TAG = "v20260501"
BASE_URL = f"https://raw.githubusercontent.com/mattiaverga/OpenNGC/{OPENNGC_TAG}/database_files/"
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "AstroPostMaster.Core" / "Catalog" / "catalog.json"
CURATED = ROOT / "tools" / "catalog_curated.json"

# Cross-identifiers worth keeping from OpenNGC's "Identifiers" column.
EXTRA_ID_PREFIXES = ("C ", "LBN ", "LDN ", "SH 2-", "B ", "Mel ", "Cr ", "vdB ")
# Primary-designation preference, lowest first.
ID_RANK = [(r"M\d", 0), (r"NGC ", 1), (r"IC ", 2), (r"Sh2-", 3), (r"C\d", 4), (r"B\d", 5)]


def fetch(name):
    with urllib.request.urlopen(BASE_URL + name) as resp:
        return list(csv.DictReader(io.StringIO(resp.read().decode("utf-8")), delimiter=";"))


def fmt_id(raw):
    """NGC0224 -> NGC 224, IC0434 -> IC 434, C009 -> C9, B033 -> B33, 'SH 2-155' -> Sh2-155, 'C 034' -> C34."""
    raw = raw.strip()
    m = re.fullmatch(r"SH 2-0*(\d+)", raw)
    if m:
        return f"Sh2-{m.group(1)}"
    m = re.fullmatch(r"([A-Za-z]+)\s*0*(\d+)([A-Za-z]*)", raw)
    if m:
        sep = "" if len(m.group(1)) == 1 else " "
        return f"{m.group(1)}{sep}{m.group(2)}{m.group(3)}"
    return raw


def key(s):
    """Mirror of TargetCatalog.Key in C#."""
    s = "".join(ch.lower() for ch in s if ch.isalnum())
    return re.sub(r"\d+", lambda m: str(int(m.group(0))), s)


def id_rank(i):
    for pattern, rank in ID_RANK:
        if re.match(pattern, i):
            return rank
    return 9


def clean_name(n):
    n = n.strip()
    return n[4:] if n.lower().startswith("the ") else n


def entry_from_row(r):
    ids = []
    if r["M"]:
        ids.append(f"M{int(r['M'])}")
    ids.append(fmt_id(r["Name"]))
    for extra in (r.get("Identifiers") or "").split(","):
        extra = extra.strip()
        if extra.startswith(EXTRA_ID_PREFIXES):
            ids.append(fmt_id(extra))
    ids = sorted(dict.fromkeys(ids), key=id_rank)
    names = [clean_name(n) for n in (r.get("Common names") or "").split(",") if n.strip()]
    return {"ids": ids, "name": names[0] if names else "", "aliases": names[1:], "type": r["Type"]}


def add_designation_aliases(e):
    for i in e["ids"]:
        if re.fullmatch(r"M\d+", i):
            e["aliases"].append("Messier " + i[1:])
        elif re.fullmatch(r"C\d+", i):
            e["aliases"].append("Caldwell " + i[1:])
    e["aliases"] = list(dict.fromkeys(a for a in e["aliases"] if a and a != e["name"]))


def sort_key(e):
    primary = e["ids"][0]
    group = 0 if re.fullmatch(r"M\d+", primary) else (1 if e["name"] else 2)
    num = re.search(r"\d+", primary)
    return (group, re.sub(r"[\d\s-].*", "", primary), int(num.group(0)) if num else 0, primary)


def main():
    rows = fetch("NGC.csv") + fetch("addendum.csv")
    entries, dups = [], []
    for r in rows:
        if r["Type"] == "NonEx":
            continue
        if r["Type"] == "Dup":
            dups.append(r)
            continue
        entries.append(entry_from_row(r))

    by_key = {}
    for e in entries:
        for i in e["ids"]:
            by_key.setdefault(key(i), e)

    # Duplicate designations (e.g. IC 11 == NGC 281) become extra ids of the object they duplicate.
    for r in dups:
        ref = ("NGC" + r["NGC"]) if r["NGC"] else ("IC" + r["IC"]) if r["IC"] else ("M" + r["M"]) if r["M"] else None
        target = by_key.get(key(ref)) if ref else None
        if target is not None:
            dup_id = fmt_id(r["Name"])
            if dup_id not in target["ids"]:
                target["ids"].append(dup_id)
                by_key.setdefault(key(dup_id), target)

    for c in json.loads(CURATED.read_text(encoding="utf-8")):
        matches = []
        for i in c["ids"]:
            e = by_key.get(key(i))
            if e is not None and all(e is not m for m in matches):
                matches.append(e)
        merged = {
            "ids": list(c["ids"]),
            "name": c.get("name") or (matches[0]["name"] if matches else ""),
            "aliases": list(c.get("aliases", [])),
            "type": c.get("type") or (matches[0]["type"] if matches else "Other"),
        }
        for m in matches:
            merged["ids"] += [i for i in m["ids"] if i not in merged["ids"]]
            if not c.get("replaceAliases"):
                merged["aliases"] += [a for a in [m["name"], *m["aliases"]] if a and a not in merged["aliases"]]
            entries[:] = [x for x in entries if x is not m]
        if c.get("hashtags"):
            merged["hashtags"] = c["hashtags"]
        entries.append(merged)
        for i in merged["ids"]:
            by_key[key(i)] = merged

    for e in entries:
        add_designation_aliases(e)
    entries.sort(key=sort_key)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    with OUT.open("w", encoding="utf-8") as f:
        f.write("[\n")
        f.write(",\n".join(json.dumps(e, ensure_ascii=False, separators=(",", ":")) for e in entries))
        f.write("\n]\n")
    print(f"wrote {len(entries)} entries to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
