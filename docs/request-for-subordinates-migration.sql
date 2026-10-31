-- YasPortal: permission "Requests.Team.Create" (ثبت درخواست برای زیرمجموعه). Idempotent; safe to run more than once.
-- It lets a manager register a request in the name of an employee who holds a position below the manager's active position.
-- Not granted to anyone here: assign it on the access page. The development database creates the row from the catalog at startup.
INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Requests.Team.Create', N'ثبت درخواست برای زیرمجموعه')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);
