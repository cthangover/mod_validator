# Validator — checking mods without playing them

The Validator reads every mod of the running game — manifests, data files, scenarios, scene patches and C# sources — and reports what is broken or suspicious: a reference to an item, scene or background nobody declares, an unknown scenario command, a wrong parameter, a missing dependency. It runs in the background each time a game starts and writes a report, so a typo is found before a player reaches the line that contains it.

It checks what can be seen in the files. It does not play the game: a scenario the Validator accepts can still behave differently from what the author meant, and code that builds ids at run time cannot be followed.

## A mistake and its correction

A scenario line that gives the player wolf meat, with a typo in the item id:

```scenario
scene: home_kitchen
priority: 0
---
action item.add id=food/wolf_meet count=1
end
```

After the next start of Cthangover the report holds:

```json
{
  "severity": "error",
  "code": "REF_NOT_FOUND",
  "message": "item 'food/wolf_meet' is referenced but never declared",
  "mod": "cthangover_game",
  "file": "scenarios/doc_validator_demo.scenario",
  "line": 4,
  "suggestion": "declare item 'food/wolf_meet' or fix the reference",
  "related": null
}
```

The mod, the file and the line point at the mistake. With `food/wolf_meat` the issue is gone and the error count in the summary goes down by one (from 18 to 17 in this run).

## Running it and reading the report

- It starts by itself when a game that includes it is entered (Cthangover includes it with its developer tools), after the mods are loaded, and runs on a background thread; the game is not blocked.
- The log gets one line per pass: `validator: started, 33 mod(s) queued`, then the totals of errors, warnings and infos.
- The report is written to `user://validation/report.json` (in the game's user data folder). It holds a summary, the checked mods with their versions and checksums, and the list of issues.

Each issue has:

- `severity` — `error` (something is broken), `warning` (probably a mistake) or `info` (worth a look, for example an item nothing refers to);
- `code` — the kind of problem, for example `REF_NOT_FOUND`, `JSON_PARSE_ERROR`, `SCHEMA_MISSING_FIELD`, `SCHEMA_UNKNOWN_PARAM`, `DECL_UNUSED`, `DYNAMIC_KEY`;
- `mod`, `file`, `line` — where to fix it; `suggestion` — what to do.

`DYNAMIC_KEY` infos mean a key is built at run time, so the Validator cannot check it; they are not errors.

## What is checked

The checks run in three phases:

1. **Schema** — `manifest.json` has a string `id` and `version`; JSON files parse.
2. **Contracts** — what each mod declares and refers to: entities of every kind (characters, items, quests, skills, recipes, actions and the kinds described by schemas), scenario commands and actions with their parameters, scene patches, properties read and written by C# code.
3. **Reconciliation** — every reference has a declaration, `depends` covers what a mod uses from others, declarations nothing uses.

Scenario actions and their parameters are checked against the schemas mods ship (see Schemas), with the same rules as the command palette.

## Settings

In the mod's settings tab of the mod manager:

- **Enabled**, **Run on start** — turn the pass off;
- **Parse C#**, **Parse GDScript** (off by default, slow), **Parse scenarios** — which sources are read;
- **Write report**, **Report path** — where `report.json` goes;
- **Block mods on error** — present in the settings, but in the current version it does not change anything: mods are never blocked.

## If something goes wrong

1. **There is no report.** The game was closed before the pass ended (it takes about half a minute for Cthangover), or Write report is off.
2. **Many `DYNAMIC_KEY` lines.** Normal for code that builds keys; they are infos.
