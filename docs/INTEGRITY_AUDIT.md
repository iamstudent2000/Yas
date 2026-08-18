# Application integrity rules

The application enforces the following rules at the domain/persistence boundary, not only in Razor UI code.

- An inactive employee cannot hold an active position assignment.
- Deactivating an employee ends all active position assignments and clears the last-position preference.
- A position can have only one active employee.
- SQL Server also enforces the one-active-employee-per-position rule with a filtered unique index, protecting concurrent requests.
- Position hierarchy cycles are rejected before persistence.
- Assignment history is opened when an assignment is created/reactivated and closed when an assignment ends.
- Authorization must validate the current employee state and the current active position assignment rather than relying only on cookie claims.
