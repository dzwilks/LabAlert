# LabAlert

LabAlert is a prototype for receiving HL7 v2 ORU^R01 lab results over MLLP,
storing them in SQL Server or Azure SQL, and showing unacknowledged flagged
results in a React UI.

**Not for clinical use as-is.** There is no authentication, authorization,
clinician identity or audit trail, transport encryption, message deduplication,
or handling of corrected/cancelled results. A lost ACK can cause a retry to
create duplicate results. Protect the API, database and MLLP endpoint and add
these controls, clinical validation, monitoring and operational procedures
before using real patient data. This prototype is not a substitute for a
clinical notification system.

## Run locally

Requirements: .NET 10 SDK, Node.js (a version supported by Vite), and an
existing SQL Server or Azure SQL database. Execute `server/schema.sql` once
in that database, using your usual SQL client. The application does not create
or migrate the schema.

Set the connection string through the environment rather than committing
credentials. For example, on a shell:

```sh
export ConnectionStrings__LabAlert='your SQL Server connection string'
dotnet run --project server/LabAlert.Api.csproj --urls http://127.0.0.1:5000
```

In another shell:

```sh
cd client
npm ci
npm run dev
```

Open the Vite URL printed in the terminal. The development server proxies
`/api` to `127.0.0.1:5000`. For deployment, serve the built `client/dist`
through a reverse proxy that routes `/api` to the .NET service. The UI shows
the current unacknowledged flagged results, filters on patient ID/name and
patient unit, and acknowledges individual results. Refresh the page to fetch
new results.

## HL7 and rules

The listener defaults to `127.0.0.1:2575`; set `Mllp__Address` to a literal
IP address and `Mllp__Port` to change it. Restrict network access: MLLP here
is plain TCP, not TLS. Messages must be UTF-8, framed with VT and FS CR, and
no larger than 1 MiB. The listener accepts multiple frames per connection and
responds with an MLLP-framed `AA` only after all results in the message commit
in one transaction; parse or database failures produce `AE` when a usable MSH
is present. A malformed frame or unusable MSH closes the connection.

NHapi parses PID-3/5 (patient ID/name), PV1-3.1 (patient unit), OBR-2/3/4
(order), and OBX-3/5/6/7/8 (test, value, units, reference range and flags).
The patient ID and OBR are required for each result. OBX-5 retains HL7
encoding for composite or repeated values; unit and test identifiers use their
first components. The location filter is based on the patient location
(PV1-3.1), not the measurement units (OBX-6). All results are stored; only
unacknowledged abnormal and critical results appear in the list.

Flags `AA`, `HH`, `LL`, and `PANIC` are critical; any other nonempty flag
except `N` is abnormal. Numeric values outside a simple inclusive `low-high`
OBX-7 range are abnormal even if the flag is `N` or absent. Other reference
range formats and non-numeric values are not interpreted. These deliberately
simple rules are not clinically validated and do not infer criticality from
numeric thresholds.
