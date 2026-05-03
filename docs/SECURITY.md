# Security Considerations

This document outlines security considerations and recommendations for the PlainSight digital signage system.

## Current Security Status

⚠️ **WARNING:** This is a development/demonstration implementation. Additional security measures are required for production deployments.

## Known Security Limitations

### 1. Update Mechanism

**Issue:** Integrity verification for self-updates.

**Status:** ✅ FIXED
- **Implementation:** The player now performs a **SHA256 checksum verification** on every update binary before applying it.
- **Enforcement:** If the hash provided by the server does not match the downloaded file, the update is aborted and logged as a failure.

### 2. Installation Script Security

**Issue:** Verification during initial setup.

**Status:** 🛡️ PARTIALLY FIXED
- **Requirement:** All installation scripts must use **HTTPS** exclusively via `api.plainsight.coronasda.church`.
- **Recommendation:** Post-setup, the player relies on the encrypted Cloudflare Tunnel and API Key validation.

### 3. Credential Management

**Issue:** Security of NAS and Database credentials.

**Status:** ✅ FIXED
- **Enforcement:** Raspberry Pi units **MUST** use a dedicated NAS account with **READ-ONLY** permissions.
- **Mitigation:** In the event of physical theft of a player, the "Read-Only" restriction prevents a compromised device from deleting content or attacking the church's central video library.

### 4. API Authentication

**Issue:** Authentication on API endpoints.

**Status:** ✅ FIXED
- **Heartbeat API:** Mandatory `X-Api-Key` validation for registered devices.
- **Screenshot Upload:** Mandatory `X-Api-Key` validation for registered devices.
- **Initial Registration:** Devices are automatically assigned a unique API key on their first heartbeat.

## Cloudflare Integration (Recommended)

Since the system is planned to use Cloudflare for domain and SSL services, the following configurations are strongly recommended:

### 1. SSL/TLS Encryption
- **Mode:** Set to **"Full (Strict)"**. This ensures that the connection is encrypted from the visitor to Cloudflare, and from Cloudflare to your origin server, using a valid certificate on your origin.
- **HSTS:** Enable HTTP Strict Transport Security to ensure browsers only interact with the server over HTTPS.

### 2. Origin Protection (Cloudflare Tunnel)
- **Recommendation:** Use `cloudflared` (Cloudflare Tunnel) to connect your server to the internet. 
- **Benefit:** This allows you to run the server without opening any inbound ports (like 80 or 443) on your firewall. Only outbound traffic to Cloudflare is required, effectively hiding your origin IP from direct attacks.

### 3. Cloudflare Access (Zero Trust)
- **Dashboard Protection:** Use Cloudflare Access to add an extra layer of authentication (e.g., Google Workspace, GitHub, or One-Time Pin) in front of the admin dashboard.
- **Configuration:** Protect the root path `/` and all subpaths, but create a "Bypass" rule for the `/api/*` path to allow devices to communicate without interactive login.

### 4. Web Application Firewall (WAF)
- **Rules:** Enable Managed Rulesets to protect against common web vulnerabilities (SQLi, XSS, etc.).
- **Rate Limiting:** Implement rate limits on `/api/device/heartbeat` to protect against DDoS or brute-force attempts.

### 5. Caching & Page Rules
- **API Exceptions:** Ensure that `/api/*` endpoints are explicitly set to **Bypass Cache** to prevent stale responses from being served to devices.

### 6. Storage (Cloudflare R2) - *Optional*
- **Recommendation:** Consider using Cloudflare R2 instead of local disk or SMB for storing:
    - Player update binaries.
    - Uploaded device screenshots.
- **Benefits:** Built-in scalability, high availability, and the ability to use **Signed URLs** for secure, time-limited access to specific files without exposing the entire bucket.

## Production Security Checklist

### Infrastructure & Network
- [ ] **Cloudflare Proxy:** Ensure the cloud icon is "Orange" (Proxied) for your domain.
- [ ] **Cloudflare Tunnel:** Deploy `cloudflared` to hide origin IP.
- [ ] **Full (Strict) SSL:** Configure end-to-end encryption.
- [ ] **Firewall:** If not using Tunnel, restrict port 443 to only allow Cloudflare IP ranges.

### Application Security
- [ ] **API Keys:** Verify all devices have unique, non-default API keys.
- [ ] **Update Verification:** Implement SHA256 checksum validation (See Section 1).
- [ ] **Secure Credentials:** Use environment variables for all secrets; never hardcode.
- [ ] **Screenshot Privacy:** Ensure screenshot storage directory is not web-accessible directly.

### Device (Player) Security
- [ ] **SSH Security:** Disable password authentication; use SSH keys only.
- [ ] **Minimal OS:** Use a hardened Raspberry Pi OS image.
- [ ] **SMB Security:** Use dedicated service accounts for SMB access with read-only permissions where possible.


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
