-- MOBILE_CONTROL (CLASSICNEW, 64 server). Idempotent: creates the table if missing, otherwise only adds missing columns.
-- Secrets are never stored in clear: PASSWORD_HASH = PBKDF2, API_KEY_HASH = SHA-256 (both written by tools/SeedMobileControl).
ALTER SESSION SET CURRENT_SCHEMA = CLASSICNEW;

DECLARE
  n NUMBER;
  PROCEDURE add_col(p_col VARCHAR2, p_def VARCHAR2) IS
    c NUMBER;
  BEGIN
    SELECT COUNT(*) INTO c FROM user_tab_columns WHERE table_name = 'MOBILE_CONTROL' AND column_name = p_col;
    IF c = 0 THEN EXECUTE IMMEDIATE 'ALTER TABLE MOBILE_CONTROL ADD (' || p_col || ' ' || p_def || ')'; END IF;
  END;
BEGIN
  SELECT COUNT(*) INTO n FROM user_tables WHERE table_name = 'MOBILE_CONTROL';
  IF n = 0 THEN
    EXECUTE IMMEDIATE 'CREATE TABLE MOBILE_CONTROL (CTRL_ID NUMBER(10) NOT NULL, CONSTRAINT PK_MOBILE_CONTROL PRIMARY KEY (CTRL_ID))';
  END IF;
  add_col('LOCA',                'VARCHAR2(10)');
  add_col('UNIT',                'VARCHAR2(20)');             -- NULL = VB app (no unit filter), filled = MVC app
  add_col('MAIN_UNIT',           'VARCHAR2(10)');             -- VB: key into MASTER_TABLE_LOCATION (BPKC, ANAN ...)
  add_col('USER_ID',             'VARCHAR2(30)');
  add_col('PASSWORD_HASH',       'VARCHAR2(200)');
  add_col('IP_ADDRESS',          'VARCHAR2(60)');             -- API 2 host
  add_col('HOSTED_PORT',         'NUMBER(5)');                -- API 2 port
  add_col('CLIENT_NAME_DISPLAY', 'VARCHAR2(100)');
  add_col('DATABASE_PORT',       'NUMBER(5)');
  add_col('DATABASE_NAME',       'VARCHAR2(60)');             -- Oracle service name (Excel "DataBase")
  add_col('SCHEMA_NAME',         'VARCHAR2(30)');
  add_col('API_KEY_HASH',        'VARCHAR2(100)');
  add_col('DOC_YEAR',            'VARCHAR2(10)');             -- MVC: DOC_YEAR filter; blank = 4-digit year at the end of SCHEMA_NAME
  add_col('DB_USER',             'VARCHAR2(60)');             -- the client's own Oracle login (API 2 connects to the client database with it)
  add_col('DB_PASSWORD_ENC',     'VARCHAR2(400)');            -- its password, ENCRYPTED (AES-GCM); never stored or logged in clear
  add_col('DB_HOST',             'VARCHAR2(60)');             -- Oracle host as API 2 sees it (blank = IP_ADDRESS)
  add_col('PHONE_ID',            'VARCHAR2(60)');
  add_col('TOKEN_MINUTES',       'NUMBER(6)');
  add_col('API_CLIENTS',         'VARCHAR2(100)');
  add_col('STATUS',              'CHAR(1) DEFAULT ''N''');
  add_col('MOBILE_ACTIVE_DATE',  'DATE');
  add_col('MOBILE_VALIDITY',     'DATE');
END;
/

DECLARE n NUMBER; BEGIN
  SELECT COUNT(*) INTO n FROM user_sequences WHERE sequence_name = 'SEQ_MOBILE_CONTROL';
  IF n = 0 THEN EXECUTE IMMEDIATE 'CREATE SEQUENCE SEQ_MOBILE_CONTROL START WITH 1 INCREMENT BY 1'; END IF;
END;
/

DECLARE n NUMBER; BEGIN
  SELECT COUNT(*) INTO n FROM user_constraints WHERE constraint_name = 'CK_MOBCTL_STATUS';
  IF n = 0 THEN EXECUTE IMMEDIATE q'[ALTER TABLE MOBILE_CONTROL ADD CONSTRAINT CK_MOBCTL_STATUS CHECK (STATUS IN ('Y','N'))]'; END IF;
  SELECT COUNT(*) INTO n FROM user_constraints WHERE constraint_name = 'CK_MOBCTL_DATES';
  IF n = 0 THEN EXECUTE IMMEDIATE 'ALTER TABLE MOBILE_CONTROL ADD CONSTRAINT CK_MOBCTL_DATES CHECK (MOBILE_VALIDITY IS NULL OR MOBILE_ACTIVE_DATE IS NULL OR MOBILE_VALIDITY >= MOBILE_ACTIVE_DATE)'; END IF;
  -- only ONE active row per login (CORETEAM has two rows: only the Y one may sign in)
  SELECT COUNT(*) INTO n FROM user_indexes WHERE index_name = 'UX_MOBCTL_ACTIVE_USER';
  IF n = 0 THEN EXECUTE IMMEDIATE q'[CREATE UNIQUE INDEX UX_MOBCTL_ACTIVE_USER ON MOBILE_CONTROL (CASE WHEN STATUS = 'Y' THEN UPPER(USER_ID) END)]'; END IF;
END;
/
