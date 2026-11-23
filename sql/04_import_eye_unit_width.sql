-- IMPORT_EYE.UNIT was VARCHAR2(4): an MVC attendance upload for a unit longer than 4 characters (BPKCH, BOARD, ANANDA ...) failed with
-- ORA-12899 and nothing was saved. API 2 writes MOBILE_CONTROL.UNIT (VARCHAR2(20)) there, so make it the same size.
-- Safe to run more than once; widening keeps every existing row as it is. Run as CLASSICNEW on every database that has CLASSICNEW.IMPORT_EYE.
DECLARE
  len NUMBER;
BEGIN
  SELECT CHAR_LENGTH INTO len FROM ALL_TAB_COLUMNS WHERE OWNER = 'CLASSICNEW' AND TABLE_NAME = 'IMPORT_EYE' AND COLUMN_NAME = 'UNIT';
  IF len < 20 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLASSICNEW.IMPORT_EYE MODIFY (UNIT VARCHAR2(20))';
    DBMS_OUTPUT.PUT_LINE('IMPORT_EYE.UNIT widened from ' || len || ' to 20 characters.');
  ELSE
    DBMS_OUTPUT.PUT_LINE('IMPORT_EYE.UNIT is already ' || len || ' characters - nothing to do.');
  END IF;
END;
/
