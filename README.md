# ClassicERPMobileV2

Database-driven replacement for `ClassicERPMobileAPI` (which read `masters.config.json`).

```
FIRST TIME   Phone --> API 1  Mobile.Gateway (central)  checks login / password / API key in CLASSICNEW.MOBILE_CONTROL
                       <-- sealed config (holds the client's database login, ENCRYPTED: the phone cannot read it)
FROM THEN    Phone --> API 2  Mobile.DataApi (the client's server)   Authorization: Bearer <saved config>
                       API 2 opens the config, connects straight to THAT client's database,
                       reads MOBILE_MASTER_CONFIG there and returns the master data it describes
```

* **API 1 is only called the first time**, and again when the saved config expires (the company's *valid-until* date; about monthly).
  Its reply is `config` (sealed), `api2BaseUrl`, `mode`, `validTo`, `refreshAfter`. Nothing in the reply shows the database name, schema, user or password.
* **API 2 has no central database and no database login of its own.** Its only setting is `DataApi__ConfigKey`.
* **Mode**: `MOBILE_CONTROL.UNIT` blank = VB (no LOCA / unit / year filter); filled = MVC (`LOCA = :loca AND UNIT = :unit`, plus `DOC_YEAR` where flagged).
* **Master setup lives in each client's own database** (`MOBILE_MASTER_CONFIG`). The phone reads `GET /mobile/v1/plan` for the list, in order.

## Keys (two, both 32+ characters, keep private)
| Setting | Where | What it does |
|---|---|---|
| `Gateway__ConfigKey` = `DataApi__ConfigKey` | API 1 and every API 2 | encrypts + signs the config; **the phone never has it** |
| `Gateway__DbPasswordKey` = MobileKeyManager *Encryption key…* | API 1 and MobileKeyManager | encrypts the client database passwords stored in MOBILE_CONTROL |

## Setup
1. Central database: run `sql\01_mobile_control.sql` (adds `DB_USER`, `DB_PASSWORD_ENC`, `DB_HOST`) and `sql\02_mobile_master_config.sql`.
2. **Each client database**: the master setup table. Either run `sql\03_client_mobile_master_config.sql` connected as the client's login, or open
   *Master data setup…* in MobileKeyManager, choose the company, *Open its database*, and answer *Yes* when it offers to create the table.
3. MobileKeyManager: *Encryption key…* (once per Windows user), then *Add company…* (database user + password, saved encrypted) and generate its API key / password.
4. API 1 environment: `Gateway__ConnectionString`, `Gateway__ConfigKey`, `Gateway__DbPasswordKey` (+ optional `Gateway__Api2Scheme`, `Gateway__EnableSwagger`).
5. API 2 environment (each client server): `DataApi__ConfigKey` (+ optional `DataApi__DbHost`, `DataApi__EnableSwagger`).
6. Phone: `POST /gateway/v1/provision` with headers `x-username`, `x-password`, `x-api-key`, `x-device-id`; **save `config`**. Then call API 2 with
   `Authorization: Bearer <config>`. Face, consent and attendance calls also send `x-app-user`.

## API 2 calls (all need `Authorization: Bearer <config>`)
| Call | Item (MOBILE_MASTER_CONFIG) |
|---|---|
| `GET /mobile/v1/plan` | the list of masters to read, in order |
| `GET /mobile/v1/masters/{key}` | READ items (masters, user-login, user-schema-link, unit) |
| `PUT /mobile/v1/masters/{key}/{id}` `{"value":"..."}` | the item's UPDATE column (employee face id) |
| `GET/POST/PUT /mobile/v1/face-recognition`, `GET/POST /mobile/v1/face-consent` | face-recognition, face-consent (OPTIONS: RequireConsent, EraseOnWithdrawn, MaxVectorLength) |
| `POST /mobile/v1/import-eye` | import-eye (OPTIONS: MaxRows, SkipDuplicates, ForceMcid, Sequence) |
| `GET /mobile/v1/auth/device-check?username=&deviceId=` | device-check |
| `POST /mobile/v1/upload/{key}` | other UPLOAD_INSERT items (never the three above) |

## What to know
* **The saved config is the key to the company's data until it expires.** There is no central check on later calls, so a company is cut off early only by
  changing that client's own database login or setup. Keep *valid-until* short (monthly) and keep `ConfigKey` private: anyone holding it can open or forge configs.
* A config made for one company cannot be changed (it is authenticated); a damaged, expired or foreign config is refused with 401.
* `launchSettings.json` files hold real keys and passwords after you fill them in: do not commit them.
* Swagger: `DataApi__EnableSwagger=true` / `Gateway__EnableSwagger=true` (off by default).

## Tested (CLASSICDEV)
First sign-in, then API 2 alone (API 1 stopped) serving the plan and masters from the client database with no database login of its own;
tampered / missing / old-style configs refused; a company without a database login refused with 503; master setup editing and table creation
in a client database; VB and MVC filters (MVC on `TMVC_*` test copies, `sql\test\`).
**Not tested:** a real MVC schema; a second server running API 2 on a different machine from the database; uploads that insert in MVC.

## The 64 server (103.234.116.64)
| Program | Port | Address |
|---|---|---|
| old Classic mobile API (unchanged) | 90 | http://103.234.116.64:90/ |
| **API 1** (Mobile.Gateway) | **91** | http://103.234.116.64:91/ - give this to the apps |
| **API 2** (Mobile.DataApi) | **92** | http://103.234.116.64:92/ - saved in MOBILE_CONTROL (IP_ADDRESS / HOSTED_PORT) for the companies on this server |

Copy publish\server64\ to the server and follow DEPLOY-64-SERVER.txt (firewall rules for 91 and 92, then run the two start scripts).
