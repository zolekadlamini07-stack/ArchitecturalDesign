-- =============================================================================================
-- Q2 CHANGE 6 (D17): ONE DATABASE ROLE PER MODULE - ownership enforced by PostgreSQL itself.
-- In Q1 every module connected with the same login, so "just one JOIN into another module's
-- table" was possible. With these roles, the database refuses it.
-- Run once by an admin; then set ConnectionStrings:<Module> to that module's login.
-- =============================================================================================
DO $$
DECLARE m text;
BEGIN
  FOREACH m IN ARRAY ARRAY['identity','customers','restaurants','ordering','payments','delivery','notifications','partner_integration'] LOOP
    EXECUTE format('CREATE ROLE %I_rw LOGIN PASSWORD %L', m, 'change-me');
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I_rw', m, m);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I_rw', m, m); -- own schema only
    EXECUTE format('GRANT USAGE ON SCHEMA jobs TO %I_rw', m);
  END LOOP;
END $$;

-- Reporting: READ-ONLY across the schemas it reports on (used against the read replica).
CREATE ROLE reporting_ro LOGIN PASSWORD 'change-me';
GRANT USAGE ON SCHEMA ordering, delivery, reporting TO reporting_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA ordering, delivery, reporting TO reporting_ro;
