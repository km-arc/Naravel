namespace NaravelApp.App.Http.Requests;

/// <summary>The body of <c>POST /register</c>. A missing field is answered with 400 before the controller runs.</summary>
public sealed record RegisterRequest(string Email, string Name);
