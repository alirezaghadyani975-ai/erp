namespace Erp.Accounting.Application.Abstractions;

public class NotFoundException(string message) : Exception(message);
