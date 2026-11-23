# ClassicERPMobileV2

Database-driven replacement for `ClassicERPMobileAPI` (which read `masters.config.json`).

```
FIRST TIME   Phone --> API 1  Mobile.Gateway (central)  checks login / password / API key in CLASSICNEW.MOBILE_CONTROL
                       <-- appConfig: API calling details only (API 2 address + end points, company, access token, expiry)
                           -> the app saves it as its config JSON file
FROM THEN    Phone reads its saved file --> API 2  Mobile.DataApi (the client's server)   Authorization: Bearer <appConfig.auth.token>
                       API 2 connects to the client's database with the login in ITS OWN settings,
                       reads MOBILE_MASTER_CONFIG there and returns the master data it describes
```

* **API 1 is called the first time**, and then by the app itself (no user action) whenever `refreshAfter` is reached.
  The token is **short-lived** (at most `Gateway__MaxTokenHours`, default 12 h; `TOKEN_MINUTES` or the valid-until date can make it shorter)
  because API 2 cannot cancel one: blocking a login or ending its validity in MOBILE_CONTROL cuts it off at its next refresh.
* **No database connection ever goes to the phone.** The access token only says which company / mode / LOCA-UNIT / schema and until when;
  it is sealed so the phone cannot read or change it.
* **API 2 has no central database.** It holds the client's database login in its own settings (`DataApi__DbService`, `DataApi__DbUser`, `DataApi__DbPassword`).

### The app's config file (`appConfig` from API 1)
```json
{
  "version": 3, "issuedAt": "...",
  "userName": "...", "passwordEnc": "", "apiKeyEnc": "",
  "controlId": 3, "clientName": "...", "mode": "VB", "loca": "...", "unit": null, "docYear": "2027",
  "api1": { "baseUrl": "http://<api1>", "provision": "/gateway/v1/provision" },
  "api2": { "baseUrl": "http://<ip>:<hosted_port>",
            "endpoints": { "plan": "/mobile/v1/plan", "masters": "/mobile/v1/masters/{key}", "updateMaster": "/mobile/v1/masters/{key}/{id}",
                           "upload": "/mobile/v1/upload/{key}", "faceRecognition": "/mobile/v1/face-recognition", "faceConsent": "/mobile/v1/face-consent",
                           "importEye": "/mobile/v1/import-eye", "deviceCheck": "/mobile/v1/auth/device-check", "health": "/health" } },
  "auth": { "header": "Authorization", "scheme": "Bearer", "token": "c3....", "appUserHeader": "x-app-user" },
  "validFrom": "...", "validTo": "...", "refreshAfter": "...", "tokenExpires": "..."
}
```
`passwordEnc` / `apiKeyEnc` come back empty: the **app** fills them with the password and API key the user typed, encrypted with the phone's
own key store (Android Keystore / iOS Keychain), before saving the file. API 1 only stores hashes, so it cannot send them.
* **Mode**: `MOBILE_CONTROL.UNIT` blank = VB (no LOCA / unit / year filter); filled = MVC (`LOCA = :loca AND UNIT = :unit`, plus `DOC_YEAR` where flagged).
* **Master setup lives in each client's own database** (`MOBILE_MASTER_CONFIG`). The phone reads `GET /mobile/v1/plan` for the list, in order.

## Keys (two, both 32+ characters, keep private)
| Setting | Where | What it does |
|---|---|---|
| `Gateway__ConfigKey` = `DataApi__ConfigKey` | API 1 and every API 2 | encrypts + signs the access token; **the phone never has it** |
| `Gateway__DbPasswordKey` = MobileKeyManager *Encryption key…* | API 1 and MobileKeyManager | encrypts the client database passwords stored in MOBILE_CONTROL |

## Setup
1. Central database: run `sql\01_mobile_control.sql` (adds `DB_USER`, `DB_PASSWORD_ENC`, `DB_HOST`) and `sql\02_mobile_master_config.sql`.
2. **Each client database**: the master setup table. Either run `sql\03_client_mobile_master_config.sql` connected as the client's login, or open
   *Master data setup…* in MobileKeyManager, choose the company, *Open its database*, and answer *Yes* when it offers to create the table.
3. MobileKeyManager: *Encryption key…* (once per Windows user), then *Add company…* (database user + password, saved encrypted) and generate its API key / password.
4. API 1 environment: `Gateway__ConnectionString`, `Gateway__ConfigKey` (+ optional `Gateway__MaxTokenHours` default 12, `Gateway__Api2Scheme`, `Gateway__EnableSwagger`).
5. API 2 environment (each client server): `DataApi__ConfigKey`, `DataApi__DbService`, `DataApi__DbUser`, `DataApi__DbPassword`
   (+ optional `DataApi__DbHost` default localhost, `DataApi__DbPort` default 1521, `DataApi__EnableSwagger`).
6. Phone: `POST /gateway/v1/provision` with headers `x-username`, `x-password`, `x-api-key` (no device id); **save `appConfig`** as the app's config file.
   The device is validated by API 2 afterwards: `GET /mobile/v1/auth/device-check?username=&deviceId=` against the customer's own database.
   From then on read the file and call `api2.baseUrl` + an end point with `Authorization: Bearer <auth.token>`. Face, consent and attendance calls also send `x-app-user`.
   When `refreshAfter` is reached, or API 2 answers 401 with `reason: "token-expired"`, call API 1 again with the saved user name / password / API key,
   replace the file and retry. If API 1 refuses (401), the login was blocked or expired: delete the file and show the sign-in screen.

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
