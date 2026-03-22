using SIV.Domain.Enums;

namespace SIV.Application.DTOs;

public record AuthResult(AuthResultType Type, string? Message = null);
