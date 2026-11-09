namespace AnnabaDigital.Domain.Entities;

public enum UserStatus { Pending, Active, Suspended, Deactivated }
public enum InvitationStatus { Pending, Accepted, Revoked }
public enum Outcome { Success, Failure, Denied }
public enum Severity { Low, Medium, High }
public enum MfaType { Totp }
public enum AccessRequestStatus { Pending, Approved, Rejected, Cancelled }
public enum AccessReviewStatus { Open, Closed }
public enum ReviewDecision { Pending, Keep, Revoke }
