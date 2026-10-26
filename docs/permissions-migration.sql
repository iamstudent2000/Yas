-- YasPortal: permission restructure (Area.Resource.Action). Idempotent; safe to run more than once.
-- Old codes are renamed IN PLACE, so every grant to persons, positions and groups is kept.
-- Users must sign out and sign in again afterwards (permissions are snapshotted into the sign-in cookie).
SET XACT_ABORT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Page.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.View')
    UPDATE Permissions SET Code = N'Dashboard.Page.View' WHERE Code = N'Dashboard.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Mine.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Mine')
    UPDATE Permissions SET Code = N'Dashboard.Mine.View' WHERE Code = N'Dashboard.Mine';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Team.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Team')
    UPDATE Permissions SET Code = N'Dashboard.Team.View' WHERE Code = N'Dashboard.Team';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.System.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.System')
    UPDATE Permissions SET Code = N'Dashboard.System.View' WHERE Code = N'Dashboard.System';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Profile.Own.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Profile.View')
    UPDATE Permissions SET Code = N'Profile.Own.View' WHERE Code = N'Profile.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Own.Create') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Create')
    UPDATE Permissions SET Code = N'Requests.Own.Create' WHERE Code = N'Requests.Create';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Own.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.View')
    UPDATE Permissions SET Code = N'Requests.Own.View' WHERE Code = N'Requests.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Team.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.ViewSubordinateRequests')
    UPDATE Permissions SET Code = N'Requests.Team.View' WHERE Code = N'Requests.ViewSubordinateRequests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.TeamApprovals.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.ViewSubordinateApprovals')
    UPDATE Permissions SET Code = N'Requests.TeamApprovals.View' WHERE Code = N'Requests.ViewSubordinateApprovals';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.Approve') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Approve')
    UPDATE Permissions SET Code = N'Requests.Decision.Approve' WHERE Code = N'Requests.Approve';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.Reject') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Reject')
    UPDATE Permissions SET Code = N'Requests.Decision.Reject' WHERE Code = N'Requests.Reject';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReturnToRequester') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.ReturnToRequester')
    UPDATE Permissions SET Code = N'Requests.Decision.ReturnToRequester' WHERE Code = N'Requests.ReturnToRequester';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReturnToPreviousStep') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.ReturnToPreviousStep')
    UPDATE Permissions SET Code = N'Requests.Decision.ReturnToPreviousStep' WHERE Code = N'Requests.ReturnToPreviousStep';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReturnToSpecificStep') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.ReturnToSpecificStep')
    UPDATE Permissions SET Code = N'Requests.Decision.ReturnToSpecificStep' WHERE Code = N'Requests.ReturnToSpecificStep';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Delegations.Own.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Delegation')
    UPDATE Permissions SET Code = N'Delegations.Own.Manage' WHERE Code = N'Requests.Delegation';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Delegations.Own.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Delegations.Create')
    UPDATE Permissions SET Code = N'Delegations.Own.Manage' WHERE Code = N'Delegations.Create';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests')
    UPDATE Permissions SET Code = N'Admin.Requests.View' WHERE Code = N'Admin.Requests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.CreateForOthers') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.RequestForOthers')
    UPDATE Permissions SET Code = N'Admin.Requests.CreateForOthers' WHERE Code = N'Admin.RequestForOthers';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.ForceClose') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.ForceClose')
    UPDATE Permissions SET Code = N'Admin.Requests.ForceClose' WHERE Code = N'Requests.ForceClose';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.Reopen') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Reopen')
    UPDATE Permissions SET Code = N'Admin.Requests.Reopen' WHERE Code = N'Requests.Reopen';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Users.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Users')
    UPDATE Permissions SET Code = N'Admin.Users.Manage' WHERE Code = N'Admin.Users';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Organizations.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Organizations')
    UPDATE Permissions SET Code = N'Admin.Organizations.Manage' WHERE Code = N'Admin.Organizations';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Positions.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Positions')
    UPDATE Permissions SET Code = N'Admin.Positions.Manage' WHERE Code = N'Admin.Positions';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Workflows.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Workflows')
    UPDATE Permissions SET Code = N'Admin.Workflows.Manage' WHERE Code = N'Admin.Workflows';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Delegations.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Delegation')
    UPDATE Permissions SET Code = N'Admin.Delegations.Manage' WHERE Code = N'Admin.Delegation';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Delegations.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Delegations.Manage')
    UPDATE Permissions SET Code = N'Admin.Delegations.Manage' WHERE Code = N'Delegations.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Access.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Access')
    UPDATE Permissions SET Code = N'Admin.Access.Manage' WHERE Code = N'Admin.Access';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Permissions.Manage') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Permissions')
    UPDATE Permissions SET Code = N'Admin.Permissions.Manage' WHERE Code = N'Admin.Permissions';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.AssignmentHistory.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.AssignmentHistory')
    UPDATE Permissions SET Code = N'Admin.AssignmentHistory.View' WHERE Code = N'Admin.AssignmentHistory';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.AuditLog.View') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.AuditLog')
    UPDATE Permissions SET Code = N'Admin.AuditLog.View' WHERE Code = N'Admin.AuditLog';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AllRequests.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AllRequests')
    UPDATE Permissions SET Code = N'Reports.AllRequests.Export' WHERE Code = N'Reports.AllRequests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.ActiveRequests.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.ActiveRequests')
    UPDATE Permissions SET Code = N'Reports.ActiveRequests.Export' WHERE Code = N'Reports.ActiveRequests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.RejectedRequests.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.RejectedRequests')
    UPDATE Permissions SET Code = N'Reports.RejectedRequests.Export' WHERE Code = N'Reports.RejectedRequests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.TeamRequests.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.TeamRequests')
    UPDATE Permissions SET Code = N'Reports.TeamRequests.Export' WHERE Code = N'Reports.TeamRequests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.MyRequests.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.MyRequests')
    UPDATE Permissions SET Code = N'Reports.MyRequests.Export' WHERE Code = N'Reports.MyRequests';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Users.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Users')
    UPDATE Permissions SET Code = N'Reports.Users.Export' WHERE Code = N'Reports.Users';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Organizations.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Organizations')
    UPDATE Permissions SET Code = N'Reports.Organizations.Export' WHERE Code = N'Reports.Organizations';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Positions.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Positions')
    UPDATE Permissions SET Code = N'Reports.Positions.Export' WHERE Code = N'Reports.Positions';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AssignmentHistory.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AssignmentHistory')
    UPDATE Permissions SET Code = N'Reports.AssignmentHistory.Export' WHERE Code = N'Reports.AssignmentHistory';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AuditLog.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AuditLog')
    UPDATE Permissions SET Code = N'Reports.AuditLog.Export' WHERE Code = N'Reports.AuditLog';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Delegations.Export') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Delegations')
    UPDATE Permissions SET Code = N'Reports.Delegations.Export' WHERE Code = N'Reports.Delegations';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Leave.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Leave')
    UPDATE Permissions SET Code = N'Forms.Leave.Print' WHERE Code = N'Forms.Leave';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Purchase.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Purchase')
    UPDATE Permissions SET Code = N'Forms.Purchase.Print' WHERE Code = N'Forms.Purchase';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Access.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Access')
    UPDATE Permissions SET Code = N'Forms.Access.Print' WHERE Code = N'Forms.Access';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Loan.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Loan')
    UPDATE Permissions SET Code = N'Forms.Loan.Print' WHERE Code = N'Forms.Loan';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Helpdesk.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Helpdesk')
    UPDATE Permissions SET Code = N'Forms.Helpdesk.Print' WHERE Code = N'Forms.Helpdesk';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.WorkReport.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.WorkReport')
    UPDATE Permissions SET Code = N'Forms.WorkReport.Print' WHERE Code = N'Forms.WorkReport';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.VehicleRequest.Print') AND EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.VehicleRequest')
    UPDATE Permissions SET Code = N'Forms.VehicleRequest.Print' WHERE Code = N'Forms.VehicleRequest';

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Page.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Dashboard.Page.View', N'مشاهده صفحه داشبورد');
ELSE
    UPDATE Permissions SET Name = N'مشاهده صفحه داشبورد' WHERE Code = N'Dashboard.Page.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Mine.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Dashboard.Mine.View', N'آمار درخواست‌های من در داشبورد');
ELSE
    UPDATE Permissions SET Name = N'آمار درخواست‌های من در داشبورد' WHERE Code = N'Dashboard.Mine.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.Team.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Dashboard.Team.View', N'آمار درخواست‌های زیرمجموعه در داشبورد');
ELSE
    UPDATE Permissions SET Name = N'آمار درخواست‌های زیرمجموعه در داشبورد' WHERE Code = N'Dashboard.Team.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Dashboard.System.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Dashboard.System.View', N'آمار کل سامانه در داشبورد');
ELSE
    UPDATE Permissions SET Name = N'آمار کل سامانه در داشبورد' WHERE Code = N'Dashboard.System.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Profile.Own.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Profile.Own.View', N'مشاهده و ویرایش پروفایل خود');
ELSE
    UPDATE Permissions SET Name = N'مشاهده و ویرایش پروفایل خود' WHERE Code = N'Profile.Own.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Own.Create')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Own.Create', N'ثبت درخواست جدید');
ELSE
    UPDATE Permissions SET Name = N'ثبت درخواست جدید' WHERE Code = N'Requests.Own.Create';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Own.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Own.View', N'مشاهده درخواست‌ها و کارتابل خود');
ELSE
    UPDATE Permissions SET Name = N'مشاهده درخواست‌ها و کارتابل خود' WHERE Code = N'Requests.Own.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Team.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Team.View', N'مشاهده درخواست‌های زیرمجموعه');
ELSE
    UPDATE Permissions SET Name = N'مشاهده درخواست‌های زیرمجموعه' WHERE Code = N'Requests.Team.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.TeamApprovals.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.TeamApprovals.View', N'مشاهده تصمیمات تایید زیرمجموعه');
ELSE
    UPDATE Permissions SET Name = N'مشاهده تصمیمات تایید زیرمجموعه' WHERE Code = N'Requests.TeamApprovals.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.Approve')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Decision.Approve', N'تأیید درخواست‌ها');
ELSE
    UPDATE Permissions SET Name = N'تأیید درخواست‌ها' WHERE Code = N'Requests.Decision.Approve';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.Reject')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Decision.Reject', N'رد درخواست‌ها');
ELSE
    UPDATE Permissions SET Name = N'رد درخواست‌ها' WHERE Code = N'Requests.Decision.Reject';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReturnToRequester')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Decision.ReturnToRequester', N'بازگرداندن درخواست به درخواست‌کننده');
ELSE
    UPDATE Permissions SET Name = N'بازگرداندن درخواست به درخواست‌کننده' WHERE Code = N'Requests.Decision.ReturnToRequester';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReturnToPreviousStep')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Decision.ReturnToPreviousStep', N'بازگرداندن درخواست به مرحله قبل');
ELSE
    UPDATE Permissions SET Name = N'بازگرداندن درخواست به مرحله قبل' WHERE Code = N'Requests.Decision.ReturnToPreviousStep';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReturnToSpecificStep')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Requests.Decision.ReturnToSpecificStep', N'بازگشت به مرحله دلخواه');
ELSE
    UPDATE Permissions SET Name = N'بازگشت به مرحله دلخواه' WHERE Code = N'Requests.Decision.ReturnToSpecificStep';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Delegations.Own.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Delegations.Own.Manage', N'تفویض اختیار سمت خود به دیگری');
ELSE
    UPDATE Permissions SET Name = N'تفویض اختیار سمت خود به دیگری' WHERE Code = N'Delegations.Own.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Requests.View', N'گزارش و جستجوی همه درخواست‌ها');
ELSE
    UPDATE Permissions SET Name = N'گزارش و جستجوی همه درخواست‌ها' WHERE Code = N'Admin.Requests.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.CreateForOthers')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Requests.CreateForOthers', N'ثبت درخواست برای دیگران');
ELSE
    UPDATE Permissions SET Name = N'ثبت درخواست برای دیگران' WHERE Code = N'Admin.Requests.CreateForOthers';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.ForceClose')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Requests.ForceClose', N'بستن اجباری درخواست‌های گیر کرده');
ELSE
    UPDATE Permissions SET Name = N'بستن اجباری درخواست‌های گیر کرده' WHERE Code = N'Admin.Requests.ForceClose';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Requests.Reopen')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Requests.Reopen', N'بازگشایی درخواست‌های ردشده');
ELSE
    UPDATE Permissions SET Name = N'بازگشایی درخواست‌های ردشده' WHERE Code = N'Admin.Requests.Reopen';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Users.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Users.View', N'مشاهده کاربران');
ELSE
    UPDATE Permissions SET Name = N'مشاهده کاربران' WHERE Code = N'Admin.Users.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Users.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Users.Manage', N'مدیریت کاربران');
ELSE
    UPDATE Permissions SET Name = N'مدیریت کاربران' WHERE Code = N'Admin.Users.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Organizations.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Organizations.View', N'مشاهده سازمان‌ها');
ELSE
    UPDATE Permissions SET Name = N'مشاهده سازمان‌ها' WHERE Code = N'Admin.Organizations.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Organizations.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Organizations.Manage', N'مدیریت سازمان‌ها');
ELSE
    UPDATE Permissions SET Name = N'مدیریت سازمان‌ها' WHERE Code = N'Admin.Organizations.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Positions.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Positions.View', N'مشاهده سمت‌ها');
ELSE
    UPDATE Permissions SET Name = N'مشاهده سمت‌ها' WHERE Code = N'Admin.Positions.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Positions.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Positions.Manage', N'مدیریت سمت‌ها');
ELSE
    UPDATE Permissions SET Name = N'مدیریت سمت‌ها' WHERE Code = N'Admin.Positions.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Workflows.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Workflows.View', N'مشاهده مراحل گردش‌کار');
ELSE
    UPDATE Permissions SET Name = N'مشاهده مراحل گردش‌کار' WHERE Code = N'Admin.Workflows.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Workflows.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Workflows.Manage', N'مدیریت مراحل گردش‌کار');
ELSE
    UPDATE Permissions SET Name = N'مدیریت مراحل گردش‌کار' WHERE Code = N'Admin.Workflows.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Delegations.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Delegations.View', N'مشاهده تفویض‌ها');
ELSE
    UPDATE Permissions SET Name = N'مشاهده تفویض‌ها' WHERE Code = N'Admin.Delegations.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Delegations.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Delegations.Manage', N'مدیریت تفویض‌ها و تعیین جانشین موقت');
ELSE
    UPDATE Permissions SET Name = N'مدیریت تفویض‌ها و تعیین جانشین موقت' WHERE Code = N'Admin.Delegations.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Access.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Access.View', N'مشاهده دسترسی‌ها');
ELSE
    UPDATE Permissions SET Name = N'مشاهده دسترسی‌ها' WHERE Code = N'Admin.Access.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Access.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Access.Manage', N'مدیریت دسترسی‌ها');
ELSE
    UPDATE Permissions SET Name = N'مدیریت دسترسی‌ها' WHERE Code = N'Admin.Access.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Permissions.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Permissions.View', N'مشاهده مجوزها و گروه‌های مجوز');
ELSE
    UPDATE Permissions SET Name = N'مشاهده مجوزها و گروه‌های مجوز' WHERE Code = N'Admin.Permissions.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.Permissions.Manage')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.Permissions.Manage', N'مدیریت مجوزها و گروه‌های مجوز');
ELSE
    UPDATE Permissions SET Name = N'مدیریت مجوزها و گروه‌های مجوز' WHERE Code = N'Admin.Permissions.Manage';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.AssignmentHistory.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.AssignmentHistory.View', N'مشاهده سوابق تخصیص سمت‌ها');
ELSE
    UPDATE Permissions SET Name = N'مشاهده سوابق تخصیص سمت‌ها' WHERE Code = N'Admin.AssignmentHistory.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Admin.AuditLog.View')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Admin.AuditLog.View', N'مشاهده گزارش رویدادها');
ELSE
    UPDATE Permissions SET Name = N'مشاهده گزارش رویدادها' WHERE Code = N'Admin.AuditLog.View';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AllRequests.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.AllRequests.Export', N'خروجی گزارش درخواست‌ها (مدیریت)');
ELSE
    UPDATE Permissions SET Name = N'خروجی گزارش درخواست‌ها (مدیریت)' WHERE Code = N'Reports.AllRequests.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.ActiveRequests.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.ActiveRequests.Export', N'خروجی درخواست‌های در جریان (مدیریت)');
ELSE
    UPDATE Permissions SET Name = N'خروجی درخواست‌های در جریان (مدیریت)' WHERE Code = N'Reports.ActiveRequests.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.RejectedRequests.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.RejectedRequests.Export', N'خروجی درخواست‌های ردشده (مدیریت)');
ELSE
    UPDATE Permissions SET Name = N'خروجی درخواست‌های ردشده (مدیریت)' WHERE Code = N'Reports.RejectedRequests.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.TeamRequests.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.TeamRequests.Export', N'خروجی درخواست‌های زیرمجموعه');
ELSE
    UPDATE Permissions SET Name = N'خروجی درخواست‌های زیرمجموعه' WHERE Code = N'Reports.TeamRequests.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.MyRequests.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.MyRequests.Export', N'خروجی درخواست‌های من');
ELSE
    UPDATE Permissions SET Name = N'خروجی درخواست‌های من' WHERE Code = N'Reports.MyRequests.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Users.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.Users.Export', N'خروجی فهرست کاربران');
ELSE
    UPDATE Permissions SET Name = N'خروجی فهرست کاربران' WHERE Code = N'Reports.Users.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Organizations.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.Organizations.Export', N'خروجی فهرست سازمان‌ها');
ELSE
    UPDATE Permissions SET Name = N'خروجی فهرست سازمان‌ها' WHERE Code = N'Reports.Organizations.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Positions.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.Positions.Export', N'خروجی فهرست سمت‌ها');
ELSE
    UPDATE Permissions SET Name = N'خروجی فهرست سمت‌ها' WHERE Code = N'Reports.Positions.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AssignmentHistory.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.AssignmentHistory.Export', N'خروجی سوابق تخصیص سمت‌ها');
ELSE
    UPDATE Permissions SET Name = N'خروجی سوابق تخصیص سمت‌ها' WHERE Code = N'Reports.AssignmentHistory.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.AuditLog.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.AuditLog.Export', N'خروجی گزارش رویدادها');
ELSE
    UPDATE Permissions SET Name = N'خروجی گزارش رویدادها' WHERE Code = N'Reports.AuditLog.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Reports.Delegations.Export')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Reports.Delegations.Export', N'خروجی فهرست تفویض‌ها');
ELSE
    UPDATE Permissions SET Name = N'خروجی فهرست تفویض‌ها' WHERE Code = N'Reports.Delegations.Export';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Leave.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.Leave.Print', N'چاپ فرم مرخصی');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم مرخصی' WHERE Code = N'Forms.Leave.Print';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Purchase.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.Purchase.Print', N'چاپ فرم درخواست خرید');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم درخواست خرید' WHERE Code = N'Forms.Purchase.Print';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Access.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.Access.Print', N'چاپ فرم درخواست دسترسی');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم درخواست دسترسی' WHERE Code = N'Forms.Access.Print';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Loan.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.Loan.Print', N'چاپ فرم درخواست وام');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم درخواست وام' WHERE Code = N'Forms.Loan.Print';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.Helpdesk.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.Helpdesk.Print', N'چاپ فرم درخواست پشتیبانی');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم درخواست پشتیبانی' WHERE Code = N'Forms.Helpdesk.Print';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.WorkReport.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.WorkReport.Print', N'چاپ فرم گزارش کار');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم گزارش کار' WHERE Code = N'Forms.WorkReport.Print';
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Forms.VehicleRequest.Print')
    INSERT INTO Permissions (Id, Code, Name) VALUES (NEWID(), N'Forms.VehicleRequest.Print', N'چاپ فرم درخواست خودرو');
ELSE
    UPDATE Permissions SET Name = N'چاپ فرم درخواست خودرو' WHERE Code = N'Forms.VehicleRequest.Print';

-- Optional: give every administrator account all new permissions that it does not have yet (admins hold permissions only through direct grants).
-- INSERT INTO EmployeePermissions (EmployeeId, PermissionId)
-- SELECT e.Id, p.Id FROM Employees e CROSS JOIN Permissions p
-- WHERE e.IsAdmin = 1 AND NOT EXISTS (SELECT 1 FROM EmployeePermissions x WHERE x.EmployeeId = e.Id AND x.PermissionId = p.Id);

COMMIT;
