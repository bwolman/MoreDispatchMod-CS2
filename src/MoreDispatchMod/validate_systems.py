#!/usr/bin/env python3
"""
Pre-build validation: cross-checks systems.lock, Systems/*.cs files, and Mod.cs registrations.

Three checks, all must pass:
  1. Every system in systems.lock has a corresponding *System.cs file in Systems/
  2. Every system in systems.lock has an UpdateAt<ClassName> in Mod.cs
  3. Every *System.cs file in Systems/ is listed in systems.lock

This catches:
  - Accidentally deleting a system file without updating systems.lock (check 1)
  - Accidentally removing an UpdateAt<> registration without updating systems.lock (check 2)
  - Adding a new system file without adding it to systems.lock (check 3)

When adding or removing a system, update systems.lock explicitly.
"""
import os
import sys

script_dir = os.path.dirname(os.path.abspath(__file__))
systems_dir = os.path.join(script_dir, "Systems")
mod_cs_path = os.path.join(script_dir, "Mod.cs")
lock_path = os.path.join(script_dir, "systems.lock")

with open(mod_cs_path) as f:
    mod_content = f.read()

with open(lock_path) as f:
    locked = [line.strip() for line in f if line.strip()]

disk_systems = {fname[:-3] for fname in os.listdir(systems_dir) if fname.endswith("System.cs")}
locked_set = set(locked)

errors = []

# Check 1: every system in lock has a .cs file on disk
for name in locked:
    if name not in disk_systems:
        errors.append(f"  MISSING FILE: {name}.cs not found in Systems/ (still in systems.lock)")

# Check 2: every system in lock has an UpdateAt<> in Mod.cs
for name in locked:
    if f"UpdateAt<{name}>" not in mod_content:
        errors.append(f"  MISSING REGISTRATION: UpdateAt<{name}> not found in Mod.cs (still in systems.lock)")

# Check 3: every .cs file on disk is in the lock
for name in sorted(disk_systems):
    if name not in locked_set:
        errors.append(f"  UNLOCKED SYSTEM: {name}.cs exists but is not listed in systems.lock")

if errors:
    print("System registration check FAILED:")
    for e in errors:
        print(e)
    print("\nTo fix: update systems.lock, Mod.cs UpdateAt<>, and Settings to match the actual system files.")
    sys.exit(1)
else:
    print(f"System registration check passed ({len(locked)} systems: {', '.join(locked)})")
