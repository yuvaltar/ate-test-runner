# ATE Test Runner

A production-line test runner in **C# / .NET 8**, in the style of the software that runs on
Automatic Test Equipment (ATE) at the end of an electronics assembly line.

It connects to a Device Under Test (DUT) over **TCP or serial (UART)**, runs a test sequence
defined in JSON, checks every measurement against limits, and writes a **PASS / FAIL report**
(CSV, JSON, HTML) per unit, named by the unit's serial number.

A **DUT simulator** (a fake power optimizer) is included, so everything runs without hardware.
It can inject faults to prove the tester really catches bad units.

```
  Ate.Cli  ──►  TestRunner  ──►  ITransport ──┬── TcpTransport ────►  DUT / simulator
  (console)     (sequence,        (line-based │                      (SCPI-style commands)
                 retries,          protocol)  └── SerialTransport ─►  real hardware (COMx)
                 stop-on-fail)
                     │
                     ▼
               ReportWriter  ──►  SN_date_PASS.csv / .json / .html
```

## Quick start

```bash
# terminal 1 - a healthy simulated unit
dotnet run --project src/Ate.DutSimulator -- --port 5025

# terminal 2 - test it
dotnet run --project src/Ate.Cli -- run --sequence sequences/opt450_end_of_line.json --tcp 127.0.0.1:5025
```

```
  [PASS] Input voltage                39.759 V in [30 .. 60]
  [PASS] Safe-mode output             0.998 V in [0.8 .. 1.2]
  [PASS] Output voltage               39.741 V in [38 .. 41]
  ...
RESULT   : PASS  (11/11 steps passed)
```

Inject a fault and the same sequence fails on the right step:

```bash
dotnet run --project src/Ate.DutSimulator -- --port 5025 --fault vout-high
```

```
  [FAIL] Output voltage               64.994 V above max 41 V (attempt 2)
  [SKIP] Output current               Skipped after earlier failure
  [SKIP] Board temperature            Skipped after earlier failure
  [PASS] Open output relay            OK          <- cleanup step always runs
RESULT   : FAIL  (8/11 steps passed)
```

Faults: `vout-high`, `selftest`, `fw-old`, `temp-timeout` (never answers), `vin-settle` (reads low twice, then settles).

Real hardware over a serial port:

```bash
dotnet run --project src/Ate.Cli -- ports
dotnet run --project src/Ate.Cli -- run --sequence sequences/opt450_end_of_line.json --serial COM3 --baud 115200
```

Exit codes: `0` PASS, `1` FAIL, `2` setup / connection error, so a line controller or CI script can act on the result.

## Test sequences

Sequences are JSON files (comments allowed), validated before any command is sent to the unit:

```jsonc
{ "name": "Output voltage", "type": "measure", "command": "MEAS:VOUT?",
  "min": 38.0, "max": 41.0, "unit": "V", "retries": 1, "delayMs": 100 }
```

| Step type | What it checks |
|-----------|----------------|
| `command` | DUT answers `OK` |
| `query`   | response equals `expectEquals` or matches `expectRegex` |
| `measure` | response is a number within `min` / `max` |

Per-step options: `retries`, `timeoutMs`, `delayMs` (settling time), `alwaysRun` (cleanup steps such as
switching a relay off still run after a failure).

## Design notes

- **No response shift after a timeout.** A background loop reads lines from the link into a channel.
  Before each command, stale lines are dropped. Without this, a late answer to a timed-out command would be
  taken as the answer to the next command and every later result would be wrong. A dedicated test
  (`TcpTransportTests`) covers it, and fails if the flush is removed.
- **One transport base class for TCP and serial.** Both are just byte streams; only opening and closing differ.
- **Pure evaluation logic.** `StepEvaluator` has no I/O, so every limit rule is unit tested directly.
- **Culture-safe parsing.** Numbers are parsed with `InvariantCulture`, so a station PC with regional
  settings that use `,` as decimal separator still reads `39.5` correctly.
- **Retries vs. connection loss.** A timeout or out-of-limit value is retried; a dropped connection is not.

## Tests

```bash
dotnet test
```

35 xUnit tests: limit evaluation, sequence validation, runner logic with a scripted fake transport,
TCP timing behaviour, and end-to-end runs against the simulator with every injected fault.
CI runs them on Ubuntu and Windows on every push (GitHub Actions).

## Project layout

```
src/Ate.Core           transports, sequence model, runner, evaluator, reports
src/Ate.Cli            console front end
src/Ate.DutSimulator   simulated power optimizer (TCP)
tests/Ate.Tests        xUnit tests
sequences/             test sequence files
```
