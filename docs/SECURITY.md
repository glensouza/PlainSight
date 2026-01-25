# Security Considerations

This document outlines security considerations and recommendations for the PlainSight digital signage system.

## Current Security Status

⚠️ **WARNING:** This is a development/demonstration implementation. Additional security measures are required for production deployments.

## Known Security Limitations

### 1. Update Mechanism (HIGH PRIORITY)

**Issue:** The self-update mechanism downloads binaries without integrity verification.

**Current Implementation:**
- Downloads update binaries over HTTP (potentially)
- No checksum verification
- No digital signature validation
- Susceptible to man-in-the-middle attacks

**Recommended Fixes:**
```csharp
// 1. Use HTTPS for all update downloads
// 2. Verify SHA256 checksum before applying update
// 3. Implement digital signature verification
// 4. Use certificate pinning for update server

public async Task PerformSelfUpdate(string updateUrl, string expectedSha256)
{
    var data = await _http.GetByteArrayAsync(updateUrl);
    
    // Verify checksum
    using var sha256 = SHA256.Create();
    var actualHash = BitConverter.ToString(sha256.ComputeHash(data));
    if (!actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new SecurityException("Update checksum verification failed");
    }
    
    // Proceed with update...
}
```

### 2. Installation Script Security

**Issue:** `install.sh` downloads binaries over HTTP without verification.

**Current Implementation:**
- Uses HTTP instead of HTTPS
- No integrity checks
- Prompts user with warning but allows to proceed

**Recommended Fixes:**
- Use HTTPS exclusively
- Verify GPG signature or checksum of downloaded binary
- Fail installation if verification fails

**Example:**
```bash
# Download checksum file
curl -L "https://${SERVER_IP}:8443/api/updates/latest/binary.sha256" -o /tmp/binary.sha256

# Download binary
curl -L "https://${SERVER_IP}:8443/api/updates/latest/binary" -o /opt/signage/Signage.Player

# Verify checksum
sha256sum -c /tmp/binary.sha256 || exit 1
```

### 3. Credential Management

**Issue:** Credentials were previously hardcoded in configuration files.

**Status:** ✅ FIXED - Now uses environment variables and credentials files.

**Current Implementation:**
- PostgreSQL password: Environment variable (required)
- SMB credentials: Environment variables (required)
- Pi credentials: Stored in `/etc/samba/signage-credentials` with 600 permissions

**Recommendations:**
- Use secrets management system (e.g., HashiCorp Vault, AWS Secrets Manager)
- Rotate credentials regularly
- Use principle of least privilege for SMB access

### 4. Content Rendering (Incomplete)

**Issue:** WebsiteRecorder service is not fully implemented.

**Status:** ⚠️ INCOMPLETE - FFmpeg integration required for production.

**Security Considerations:**
- Sanitize URLs before rendering
- Implement resource limits (CPU, memory, time)
- Run in isolated container/sandbox
- Validate content before distribution

### 5. API Authentication

**Issue:** No authentication or authorization on API endpoints.

**Current Status:** Open API endpoints.

**Recommendations:**
```csharp
// Implement API key authentication
[ApiKey]
[HttpPost("heartbeat")]
public async Task<IActionResult> Heartbeat([FromBody] DeviceTelemetryDto data)
{
    // Validate API key from header
    // Process heartbeat
}

// Device-specific API keys
public class Device
{
    public string ApiKey { get; set; } // Hashed, unique per device
}
```

### 6. Screenshot Upload (Not Implemented)

**Issue:** Screenshot capture exists but upload functionality is missing.

**Security Considerations for Implementation:**
- Use authenticated endpoint
- Validate file size limits
- Sanitize filenames
- Store in secure location with access controls
- Consider privacy implications of screenshot data

## Production Security Checklist

### Required for Production

- [ ] **Enable HTTPS/TLS**
  - Use Let's Encrypt or corporate certificates
  - Configure reverse proxy (nginx, Traefik)
  - Redirect HTTP to HTTPS

- [ ] **Implement Update Verification**
  - SHA256 checksum validation
  - Digital signature verification
  - HTTPS-only downloads

- [ ] **Secure Credentials**
  - Use secrets management system
  - Rotate all default passwords
  - Use strong passwords (20+ characters)
  - Never commit secrets to git

- [ ] **API Authentication**
  - Implement API key or OAuth2
  - Rate limiting
  - Request validation

- [ ] **Database Security**
  - Use encrypted connections (SSL/TLS)
  - Restrict network access
  - Regular backups
  - Enable audit logging

- [ ] **Container Security**
  - Run as non-root user
  - Use minimal base images
  - Regular security updates
  - Scan images for vulnerabilities

- [ ] **Network Security**
  - Use firewall rules
  - Segment networks
  - VPN for remote access
  - Monitor network traffic

- [ ] **Monitoring & Logging**
  - Centralized logging
  - Security event monitoring
  - Alerting for suspicious activity
  - Regular security audits

### Recommended

- [ ] **Content Security Policy**
  - Validate URLs before rendering
  - Sandbox content rendering
  - Resource limits

- [ ] **File Integrity Monitoring**
  - Monitor critical files for changes
  - Alert on unexpected modifications

- [ ] **Automated Security Scanning**
  - SAST (Static Application Security Testing)
  - DAST (Dynamic Application Security Testing)
  - Dependency vulnerability scanning

- [ ] **Incident Response Plan**
  - Document security procedures
  - Define escalation path
  - Regular drills

## Compliance Considerations

Depending on your organization and deployment:

- **GDPR/Privacy:** Screenshot data may contain personal information
- **PCI DSS:** If processing payment information (unlikely for signage)
- **HIPAA:** If used in healthcare settings
- **SOC 2:** For service providers

## Reporting Security Issues

If you discover a security vulnerability:

1. **Do NOT** open a public GitHub issue
2. Email security contact (configure this for your organization)
3. Include detailed description and reproduction steps
4. Allow time for patch before disclosure

## Additional Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [CIS Docker Benchmark](https://www.cisecurity.org/benchmark/docker)
- [.NET Security Best Practices](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [Docker Security](https://docs.docker.com/engine/security/)

## Version History

- **v1.0** (2026-01-25): Initial security documentation
  - Documented known limitations
  - Fixed hardcoded credentials
  - Added security warnings in code
