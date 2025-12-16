#!/usr/bin/env dotnet-script
#r "nuget: BCrypt.Net-Next, 4.0.3"

using BCrypt.Net;

var hash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
var password = "admin";

Console.WriteLine($"Testing password: '{password}'");
Console.WriteLine($"Against hash: {hash}");
Console.WriteLine($"Result: {BCrypt.Verify(password, hash)}");

// Generate a fresh hash
var newHash = BCrypt.HashPassword("admin", 11);
Console.WriteLine($"\nFresh hash for 'admin': {newHash}");
Console.WriteLine($"Verify fresh hash: {BCrypt.Verify("admin", newHash)}");

