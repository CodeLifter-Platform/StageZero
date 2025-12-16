var hash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
var password = "admin";

Console.WriteLine($"Testing password: '{password}'");
Console.WriteLine($"Against hash: {hash}");
Console.WriteLine($"Result: {BCrypt.Net.BCrypt.Verify(password, hash)}");

// Generate a fresh hash
var newHash = BCrypt.Net.BCrypt.HashPassword("admin", 11);
Console.WriteLine($"\nFresh hash for 'admin': {newHash}");
Console.WriteLine($"Verify fresh hash: {BCrypt.Net.BCrypt.Verify("admin", newHash)}");

