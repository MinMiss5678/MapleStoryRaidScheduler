namespace Application.Exceptions;

/// <summary>業務邏輯例外的基底類別，對應 4xx 錯誤</summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>資源不存在 → 404</summary>
public class NotFoundException(string message) : AppException(message);

/// <summary>業務規則違反 → 400</summary>
public class BusinessException(string message) : AppException(message);

/// <summary>權限不足 → 403</summary>
public class ForbiddenException(string message) : AppException(message);
