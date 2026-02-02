-- Insert test admin (will auto-login bypass for testing)
INSERT OR IGNORE INTO AdminUsers (Id, GoogleId, Email, Name, CreatedAt) 
VALUES (1, 'test-google-id', 'admin@test.com', 'Test Admin', datetime('now'));

-- Insert test volunteers
INSERT OR IGNORE INTO Volunteers (Id, Name, Email, Phone, IsActive, CreatedAt) 
VALUES 
(1, 'John Smith', 'john@test.com', '555-0101', 1, datetime('now')),
(2, 'Jane Doe', 'jane@test.com', '555-0102', 1, datetime('now')),
(3, 'Bob Wilson', 'bob@test.com', '555-0103', 1, datetime('now'));

-- Insert test shifts for next week
INSERT OR IGNORE INTO Shifts (Id, Date, TimeSlotId, Role, VolunteerId, Backup1VolunteerId, Status, AssignedAt) 
VALUES 
(1, date('now', '+1 day'), 1, 0, 1, 2, 1, datetime('now')),
(2, date('now', '+1 day'), 2, 1, NULL, NULL, 0, NULL),
(3, date('now', '+2 days'), 1, 0, 2, NULL, 1, datetime('now')),
(4, date('now', '+2 days'), 2, 0, NULL, NULL, 0, NULL),
(5, date('now', '+3 days'), 1, 1, 3, 1, 2, datetime('now'));

-- Insert a pending request
INSERT OR IGNORE INTO ShiftRequests (Id, ShiftId, VolunteerId, RequestedSlot, Status, RequestedAt)
VALUES (1, 2, 1, 0, 0, datetime('now'));
