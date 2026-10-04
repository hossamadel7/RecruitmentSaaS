-- Identity counters copied from SQL Server (IDENT_CURRENT), so the next value matches exactly.
SELECT setval(pg_get_serial_sequence('"_template"."Leads"', 'LeadSequence'), 1, false);
SELECT setval(pg_get_serial_sequence('"demorecruitment"."Leads"', 'LeadSequence'), 5, true);
