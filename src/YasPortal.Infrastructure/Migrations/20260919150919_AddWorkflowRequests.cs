using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YasPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivePositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequesterEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRequests_Employees_RequesterEmployeeId",
                        column: x => x.RequesterEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowRequests_Positions_RequesterPositionId",
                        column: x => x.RequesterPositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStepDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApproverRuleKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ManagerLevel = table.Column<int>(type: "int", nullable: true),
                    ApproverPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStepDefinitions_Positions_ApproverPositionId",
                        column: x => x.ApproverPositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRequestSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApproverPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRequestSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRequestSteps_Employees_ActedByEmployeeId",
                        column: x => x.ActedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowRequestSteps_Positions_ApproverPositionId",
                        column: x => x.ApproverPositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowRequestSteps_WorkflowRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "WorkflowRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowRequestSteps_WorkflowStepDefinitions_StepDefinitionId",
                        column: x => x.StepDefinitionId,
                        principalTable: "WorkflowStepDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EmployeeId",
                table: "AuditEntries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequests_RequesterEmployeeId_CreatedAtUtc",
                table: "WorkflowRequests",
                columns: new[] { "RequesterEmployeeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequests_RequesterPositionId",
                table: "WorkflowRequests",
                column: "RequesterPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequests_WorkflowType_Status",
                table: "WorkflowRequests",
                columns: new[] { "WorkflowType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequestSteps_ActedByEmployeeId",
                table: "WorkflowRequestSteps",
                column: "ActedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequestSteps_ApproverPositionId_Status",
                table: "WorkflowRequestSteps",
                columns: new[] { "ApproverPositionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequestSteps_RequestId_Order",
                table: "WorkflowRequestSteps",
                columns: new[] { "RequestId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequestSteps_StepDefinitionId",
                table: "WorkflowRequestSteps",
                column: "StepDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepDefinitions_ApproverPositionId",
                table: "WorkflowStepDefinitions",
                column: "ApproverPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepDefinitions_WorkflowType_Order",
                table: "WorkflowStepDefinitions",
                columns: new[] { "WorkflowType", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "WorkflowRequestSteps");

            migrationBuilder.DropTable(
                name: "WorkflowRequests");

            migrationBuilder.DropTable(
                name: "WorkflowStepDefinitions");
        }
    }
}
