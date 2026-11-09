-- MOBILE_MASTER_CONFIG: what API 2 reads/writes, in which order, per app mode. Replaces masters.config.json.
-- CTRL_ID NULL = default for every client; CTRL_ID set = override for that client (same APP_MODE + ITEM_KEY wins).
ALTER SESSION SET CURRENT_SCHEMA = CLASSICNEW;

DECLARE n NUMBER; BEGIN
  SELECT COUNT(*) INTO n FROM user_tables WHERE table_name = 'MOBILE_MASTER_CONFIG';
  IF n = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE MOBILE_MASTER_CONFIG (
        CFG_ID          NUMBER(10)      NOT NULL,
        CTRL_ID         NUMBER(10),
        APP_MODE        VARCHAR2(3)     NOT NULL,
        SEQ_NO          NUMBER(5)       NOT NULL,
        ITEM_KEY        VARCHAR2(40)    NOT NULL,
        DESCRIPTION     VARCHAR2(100),
        OP_TYPE         VARCHAR2(15)    NOT NULL,
        SCHEMA_SOURCE   VARCHAR2(10)    DEFAULT 'CLIENT' NOT NULL,
        TABLE_NAME      VARCHAR2(30)    NOT NULL,
        COLUMN_LIST     VARCHAR2(1000),
        ALIASES         VARCHAR2(200),
        FILTER_COLUMN   VARCHAR2(30),
        FILTER_VALUES   VARCHAR2(100),
        UNIT_FILTER     CHAR(1)         DEFAULT 'N' NOT NULL,
        REQUIRE_LISTING CHAR(1)         DEFAULT 'N' NOT NULL,
        ALLOW_CLIENTS   CHAR(1)         DEFAULT 'Y' NOT NULL,
        UPDATE_KEY_COL  VARCHAR2(30),
        UPDATE_COL      VARCHAR2(30),
        UPDATE_MAXLEN   NUMBER(5),
        ACTIVE          CHAR(1)         DEFAULT 'Y' NOT NULL,
        REMARKS         VARCHAR2(300),
        CONSTRAINT PK_MOBILE_MASTER_CONFIG PRIMARY KEY (CFG_ID),
        CONSTRAINT FK_MOBCFG_CTRL FOREIGN KEY (CTRL_ID) REFERENCES MOBILE_CONTROL (CTRL_ID),
        CONSTRAINT CK_MOBCFG_MODE CHECK (APP_MODE IN ('VB','MVC')),
        CONSTRAINT CK_MOBCFG_OP   CHECK (OP_TYPE IN ('CONTROL','READ','UPLOAD_INSERT','UPDATE_COLUMN')),
        CONSTRAINT CK_MOBCFG_SRC  CHECK (SCHEMA_SOURCE IN ('CLIENT','CLASSICNEW')),
        CONSTRAINT CK_MOBCFG_YN   CHECK (UNIT_FILTER IN ('Y','N') AND REQUIRE_LISTING IN ('Y','N') AND ALLOW_CLIENTS IN ('Y','N') AND ACTIVE IN ('Y','N'))
      )]';
    EXECUTE IMMEDIATE 'CREATE SEQUENCE SEQ_MOBILE_MASTER_CONFIG START WITH 1000 INCREMENT BY 1';
    EXECUTE IMMEDIATE 'CREATE UNIQUE INDEX UX_MOBCFG_ITEM ON MOBILE_MASTER_CONFIG (NVL(CTRL_ID,0), APP_MODE, ITEM_KEY)';
  END IF;
END;
/

-- DOC_YEAR_FILTER: MVC also filters by DOC_YEAR, only for tables that have that column (per the Excel: M_TEA_ESTATE, M_HAZIRATYPE).
DECLARE n NUMBER; BEGIN
  SELECT COUNT(*) INTO n FROM user_tab_columns WHERE table_name = 'MOBILE_MASTER_CONFIG' AND column_name = 'DOC_YEAR_FILTER';
  IF n = 0 THEN EXECUTE IMMEDIATE q'[ALTER TABLE MOBILE_MASTER_CONFIG ADD (DOC_YEAR_FILTER CHAR(1) DEFAULT 'N' NOT NULL)]'; END IF;
END;
/

-- OPTIONS: "Key=Value;Key=Value" settings of one item (face rules, import-eye limits ...) - what used to sit in masters.config.json.
DECLARE n NUMBER; BEGIN
  SELECT COUNT(*) INTO n FROM user_tab_columns WHERE table_name = 'MOBILE_MASTER_CONFIG' AND column_name = 'OPTIONS';
  IF n = 0 THEN EXECUTE IMMEDIATE 'ALTER TABLE MOBILE_MASTER_CONFIG ADD (OPTIONS VARCHAR2(300))'; END IF;
  -- OP_TYPE also allows DEVICE_CHECK
  SELECT COUNT(*) INTO n FROM user_constraints WHERE constraint_name = 'CK_MOBCFG_OP';
  IF n > 0 THEN EXECUTE IMMEDIATE 'ALTER TABLE MOBILE_MASTER_CONFIG DROP CONSTRAINT CK_MOBCFG_OP'; END IF;
  EXECUTE IMMEDIATE q'[ALTER TABLE MOBILE_MASTER_CONFIG ADD CONSTRAINT CK_MOBCFG_OP CHECK (OP_TYPE IN ('CONTROL','READ','UPLOAD_INSERT','UPDATE_COLUMN','DEVICE_CHECK'))]';
END;
/

-- Seed (defaults, CTRL_ID NULL). Safe to re-run: a row is added only if (mode,item) is missing.
-- Columns / filters are those of the old masters.config.json; table names per the Excel "At a Glance" sheet.
DECLARE
  PROCEDURE ins(p_mode VARCHAR2, p_seq NUMBER, p_key VARCHAR2, p_desc VARCHAR2, p_op VARCHAR2, p_src VARCHAR2, p_tbl VARCHAR2,
                p_cols VARCHAR2 DEFAULT NULL, p_alias VARCHAR2 DEFAULT NULL, p_fcol VARCHAR2 DEFAULT NULL, p_fval VARCHAR2 DEFAULT NULL,
                p_unit VARCHAR2 DEFAULT 'N', p_list VARCHAR2 DEFAULT 'N', p_clients VARCHAR2 DEFAULT 'Y',
                p_ukey VARCHAR2 DEFAULT NULL, p_ucol VARCHAR2 DEFAULT NULL, p_active VARCHAR2 DEFAULT 'Y', p_rem VARCHAR2 DEFAULT NULL,
                p_opts VARCHAR2 DEFAULT NULL) IS
    n NUMBER;
  BEGIN
    SELECT COUNT(*) INTO n FROM MOBILE_MASTER_CONFIG WHERE CTRL_ID IS NULL AND APP_MODE = p_mode AND ITEM_KEY = p_key;
    IF n = 0 THEN
      INSERT INTO MOBILE_MASTER_CONFIG (CFG_ID, CTRL_ID, APP_MODE, SEQ_NO, ITEM_KEY, DESCRIPTION, OP_TYPE, SCHEMA_SOURCE, TABLE_NAME, COLUMN_LIST, ALIASES,
             FILTER_COLUMN, FILTER_VALUES, UNIT_FILTER, REQUIRE_LISTING, ALLOW_CLIENTS, UPDATE_KEY_COL, UPDATE_COL, UPDATE_MAXLEN, ACTIVE, REMARKS, OPTIONS)
      VALUES (SEQ_MOBILE_MASTER_CONFIG.NEXTVAL, NULL, p_mode, p_seq, p_key, p_desc, p_op, p_src, p_tbl, p_cols, p_alias,
             p_fcol, p_fval, p_unit, p_list, p_clients, p_ukey, p_ucol, CASE WHEN p_ucol IS NULL THEN NULL ELSE 4000 END, p_active, p_rem, p_opts);
    END IF;
  END;
  PROCEDURE seed(md VARCHAR2) IS
    u VARCHAR2(1) := CASE WHEN md = 'MVC' THEN 'Y' ELSE 'N' END;   -- MVC: LOCA/UNIT filter
    l VARCHAR2(1) := CASE WHEN md = 'VB'  THEN 'Y' ELSE 'N' END;   -- VB : table must be listed in MASTER_TABLE_LOCATION
  BEGIN
    ins(md,  10, 'control', 'Control table (schema / main unit)', 'CONTROL', 'CLASSICNEW',
        CASE WHEN md = 'MVC' THEN 'CLASSIC_CONTROL' ELSE 'WAGES_CONTROL' END, p_rem => 'resolved by API 1');
    ins(md,  20, 'user-schema-link', 'User module / company access', 'READ', 'CLASSICNEW', 'USER_SCHEMA_LINK',
        'LOCA,UNIT,USER_ORA,MODULE_CODE,MODULE_NAME,API_ALLOW', p_fcol => 'MODULE_CODE', p_fval => 'GP');
    -- device-check: first column = user column, second = device column. A user registered on 2+ devices (or another device) may not sign in.
    ins(md,  25, 'device-check', 'Device check (USER_JAG.PHONE_ID)', 'DEVICE_CHECK', 'CLASSICNEW', 'USER_JAG', 'USER_ORA,PHONE_ID');
    ins(md,  30, 'user-login', 'User login (roles CL and SU)', 'READ', 'CLASSICNEW', 'USER_JAG', 'USER_ORA,USER_NAME,USER_ROLE',
        'PASSWORD=REMARKS', 'USER_ROLE', 'CL,SU');
    IF md = 'MVC' THEN
      ins(md, 40, 'unit', 'Unit master', 'READ', 'CLIENT', 'M_MAST_UNIT', 'ID,LOCA,UNIT,CODE,DESCRIPTION,PTYPE', p_rem => 'defines the units of the company');
    END IF;
    ins(md,  50, 'tea-estate',  'Tea Estate Master', 'READ', 'CLIENT', 'M_TEA_ESTATE', 'CODE,DESCN,PTYPE', p_unit => u, p_list => l);
    ins(md,  60, 'hazira-type', 'Hazira Type', 'READ', 'CLIENT', 'M_HAZIRATYPE', 'CODE,DESCN,PTYPE,SCHEMA_TYPE', p_unit => u);
    ins(md,  70, 'division',    'Division Master', 'READ', 'CLIENT', 'M_DVSN', 'CODE,DESCN,TEA_ESTATE,PTYPE', p_unit => u, p_list => l);
    ins(md,  80, 'dek',         'Attendance Book / Dek Master', 'READ', 'CLIENT', 'M_DEK', 'CODE,DESCN,DVSN_CODE,PTYPE,LEG_CODE', p_unit => u, p_list => l);
    ins(md,  90, 'sardar',      'Sardar Master', 'READ', 'CLIENT', 'M_SARDAR',   -- Excel says M_SARDR for VB, but the schema (and the old config) has M_SARDAR
        'CODE,DESCN,DEK_CODE,DVSN_CODE,PTYPE', p_unit => u, p_list => l);
    ins(md, 100, 'employee',    'Employee Master', 'READ', 'CLIENT', 'M_EMPL', 'EMPCD,LEG_CODE,NAME,DVSN_CODE,DEK_CODE,SARD_CODE,KMCD,ECD,PTYPE',
        p_fcol => 'ACTIVE_TAG', p_fval => 'Y', p_unit => u, p_list => l, p_clients => 'N', p_ukey => 'EMPCD', p_ucol => 'EMPCD_FACE_ID');
    ins(md, 110, 'kamjari',     'JOB / Kamjari Master', 'READ', 'CLIENT', 'M_KAMJARI', 'KMCD,LEG_CODE,DESCN,KM_TYPE,T_BASED,T_UNIT,REQ_SEC,FG,PTYPE', p_unit => u, p_list => l);
    ins(md, 120, 'section',     'Section Master', 'READ', 'CLIENT', 'M_SECT', 'CODE,DESCN,AREA,DVSN_CODE,PTYPE', p_unit => u, p_list => l);
    ins(md, 130, 'leaf-type',   'Leaf Type', 'READ', 'CLIENT', 'M_ELPTYP', 'ELPCODE,LEG_CODE,CODE,DESCN,SRLNO,APP_TASK,CASH,PTYPE,TAG', p_unit => u);
    ins(md, 140, 'moisture',    'Moisture / Tare Master', 'READ', 'CLIENT', 'M_MOISTURE',
        'TEA_ESTATE,EFF_DATE,MOISTURE_PER,STD_DEDUCTION,TARE_WEIGHT,PRECISION,ROUND_OFF,NET_KG_ROFF,ROFF_TYPE,PROD_DEVICE,ATTN_FACE,SHOW_ACT_WEIGHT,BT_TOLERANCE', p_unit => u);
    -- face-consent / face-recognition / import-eye are served by dedicated calls (rules below), not the generic upload
    ins(md, 200, 'face-consent',     'Face consent from mobile', 'UPLOAD_INSERT', 'CLIENT', 'M_EMPL_FACE_CONSENT',
        'EVENT_ID,EMPCD,STATE,AT,BY_USER,DEVICE_ID,WORDING_VERSION', p_unit => u, p_opts => 'EraseOnWithdrawn=Y');
    ins(md, 210, 'face-recognition', 'Face enrolment from mobile', 'UPLOAD_INSERT', 'CLIENT', 'M_EMPL_FACERECOGNITION',
        'FACE_ID,EMPCD,MODEL_ID,VECTOR,STATUS,ENROLLED_AT,ENROLLED_BY,DEVICE_ID,UPDATED_AT', p_unit => u,
        p_opts => 'RequireConsent=Y;MaxVectorLength=4000');
    -- LOCA / UNIT are not listed: API 2 fills them from the control row (MVC), the phone cannot choose them
    ins(md, 220, 'import-eye', 'Attendance import (IMPORT_EYE)', 'UPLOAD_INSERT', 'CLASSICNEW', 'IMPORT_EYE',
        'RECTYPE,TRANSDATE,TEAESTATE,DIVISION,DEK,EMP_DIV,ECODE,NAME,PFNO,JCODE,TYPE,SUBCODE,ATTENDANCE,OPTRCODE,MCID,T,LEAF,PTYPE,ENTRY,' ||
        'H,DIV1,SEC1,NETTWT1,NETTWT1_AC,GRWT1,TIME1,TIKA1,TM,H2,DIV2,SEC2,NETTWT2,NETTWT2_AC,GRWT2,TIME2,TIKA2,TM2,' ||
        'H3,DIV3,SEC3,NETTWT3,NETTWT3_AC,GRWT3,TIME3,TIKA3,TM3,DIV4,SEC4,NETTWT4,NETTWT4_AC,GRWT4,TIME4,TIKA4,' ||
        'TOTALNETT,TOTALGRWT,TASK,NIRIKH,STARTTIME,ENDTIME,RATE,U_NAME,U_ENTDT,U_NAMENEW,U_ENTDTNEW,O_USER,T_ID,DTAG,O_USERNEW,T_IDNEW,' ||
        'TUOM,CASH,SARD_CODE,HAZIRA_CODE,ID,OT_HRS,APP_RECORD_ID', p_unit => u,
        p_opts => 'MaxRows=500;SkipDuplicates=Y;ForceMcid=M001', p_rem => 'duplicates (same estate, hazira_code, date, employee, app_record_id) are not saved again');
    ins(md, 230, 'machine', 'Machine id', 'READ', 'CLIENT', 'M_MACHINE', p_active => 'N',
        p_rem => 'Missing in Excel - machine id is kept in the app; confirm before enabling');
  END;
BEGIN
  seed('VB');
  seed('MVC');
  UPDATE MOBILE_MASTER_CONFIG SET DOC_YEAR_FILTER = 'Y'
   WHERE CTRL_ID IS NULL AND APP_MODE = 'MVC' AND ITEM_KEY IN ('tea-estate', 'hazira-type');
  -- corrections for rows seeded by an earlier version of this script
  UPDATE MOBILE_MASTER_CONFIG SET TABLE_NAME = 'M_SARDAR' WHERE ITEM_KEY = 'sardar' AND TABLE_NAME = 'M_SARDR';
  UPDATE MOBILE_MASTER_CONFIG SET COLUMN_LIST = 'LOCA,UNIT,USER_ORA,MODULE_CODE,MODULE_NAME,API_ALLOW', FILTER_COLUMN = 'MODULE_CODE', FILTER_VALUES = 'GP'
   WHERE ITEM_KEY = 'user-schema-link' AND COLUMN_LIST IS NULL;
  UPDATE MOBILE_MASTER_CONFIG SET COLUMN_LIST = 'ID,LOCA,UNIT,CODE,DESCRIPTION,PTYPE' WHERE ITEM_KEY = 'unit' AND COLUMN_LIST IS NULL;
  UPDATE MOBILE_MASTER_CONFIG SET OPTIONS = 'EraseOnWithdrawn=Y' WHERE ITEM_KEY = 'face-consent' AND OPTIONS IS NULL;
  UPDATE MOBILE_MASTER_CONFIG SET OPTIONS = 'RequireConsent=Y;MaxVectorLength=4000' WHERE ITEM_KEY = 'face-recognition' AND OPTIONS IS NULL;
  UPDATE MOBILE_MASTER_CONFIG SET OPTIONS = 'MaxRows=500;SkipDuplicates=Y;ForceMcid=M001' WHERE ITEM_KEY = 'import-eye' AND OPTIONS IS NULL;
  -- VB never filters by LOCA / UNIT / DOC_YEAR
  UPDATE MOBILE_MASTER_CONFIG SET UNIT_FILTER = 'N', DOC_YEAR_FILTER = 'N' WHERE APP_MODE = 'VB';
  COMMIT;
END;
/
-- Per-client override (e.g. Bengal Tea: PTYPE W,S only): copy the default row with CTRL_ID set, then change FILTER_COLUMN / FILTER_VALUES.
