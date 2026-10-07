/*
    Deploys the LT ODM sign-in, role and menu schema in order, stopping on the first error.
    Run from the db folder (the :r paths are relative to the current directory):

      cd db
      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i deploy-auth.sql

    In SSMS: enable Query > SQLCMD Mode and run the files below in this order instead.

    sqlcmd starts with QUOTED_IDENTIFIER OFF, which filtered indexes and the stored
    procedures need ON, so the SET options are fixed here for the whole session.
*/
:on error exit
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
:r tables/auth.tables.sql
:r tables/nav.tables.sql
:r procedures/auth.procedures.sql
:r procedures/admin.procedures.sql
:r seed/auth.roles.sql
:r seed/nav.menu.sql
PRINT 'Auth, roles and menu deployed.';
