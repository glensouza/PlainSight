# GitHub Actions Workflow Documentation

This document describes the CI/CD pipeline for PlainSight using GitHub Actions.

## Overview

The PlainSight project uses GitHub Actions for:
- Building Docker images for the server
- Building ARM64 binaries for Raspberry Pi players
- Managing image retention (5 previous versions)
- Automated deployment to production server

## Workflow Files

- `.github/workflows/server.yml`: Handles server Docker builds and production deployment.
- `.github/workflows/player.yml`: Handles Raspberry Pi player ARM64 builds and release management.

## Workflow Jobs

### 1. Server CI/CD (`server.yml`)

**Trigger**: 
- Push to `main` branch (specifically for server or shared code)
- Manual workflow dispatch

**Runner**: `self-hosted` (must be the production Mac runner)

**Steps**:
1. **Checkout**: Pulls latest code into the runner's workspace.
2. **Build**: Builds a local Docker image tagged with the short Git SHA.
3. **Deploy**:
   - Tags the existing `current` image as `previous` (for rollback).
   - Tags the new SHA-tagged image as `current`.
   - Runs `docker compose up -d plainsight-server` using secrets injected into the environment.
4. **Health Check**: Pings `http://localhost:8080/health` for up to 2 minutes.
5. **Auto-Rollback**: If health check fails, re-tags `previous` as `current` and restarts.
6. **Cleanup**: Retains only the 5 most recent SHA-tagged images.

**Required Secrets**:
- `POSTGRES_PASSWORD`: Database password.
- `CLOUDFLARE_TUNNEL_TOKEN`: Token for Cloudflare Tunnel.
- `PGADMIN_DEFAULT_EMAIL`: pgAdmin login.
- `PGADMIN_DEFAULT_PASSWORD`: pgAdmin password.

### 2. Player Release (`player.yml`)

**Trigger**: Tags starting with `v` (e.g., `v1.0.0`)

**Runner**: `self-hosted` (must have write access to the SMB share mount)

**Steps**:
1. Checkout repository
2. Setup .NET 10 SDK
3. Build Player for ARM64 (linux-arm64) as single-file binary
4. Compute SHA-256 hash of binary
5. Create signed manifest JSON using `secrets.PLAINSIGHT_SIGNING_KEY`
6. Write binary and manifest to `${{ vars.UPDATES_PATH }}` on the share.
7. Create GitHub Release with assets.

## Configuration

### Required Secrets

- `POSTGRES_PASSWORD`: Database password for server deployment.
- `CLOUDFLARE_TUNNEL_TOKEN`: Cloudflare Tunnel token.
- `PGADMIN_DEFAULT_EMAIL`: pgAdmin admin email.
- `PGADMIN_DEFAULT_PASSWORD`: pgAdmin admin password.
- `PLAINSIGHT_SIGNING_KEY`: ECDSA P-256 private key for player updates.

### Required Variables

- `UPDATES_PATH`: Absolute path to the `updates` folder on the runner's machine (e.g., `/Users/admin/MyCloudHome/PlainSight/updates`).

### Environment Variables

```yaml
env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}/plainsight-server
```

## Image Retention Policy

The workflow automatically deletes old Docker images, keeping only the 5 most recent versions. This provides:
- Quick rollback capability
- Disk space management
- Historical reference

**Implementation**:
```yaml
- name: Clean up old images (keep 5 most recent)
  uses: actions/delete-package-versions@v5
  with:
    package-name: 'plainsight-server'
    package-type: 'container'
    min-versions-to-keep: 5
```

## Usage

### Deploying Server Updates

1. Make changes to server code
2. Commit and push to `main` branch
3. GitHub Actions automatically builds and pushes new Docker image
4. (Optional) Deploy to production via self-hosted runner

```bash
git add .
git commit -m "Update server feature"
git push origin main
```

### Releasing Player Updates

1. Make changes to player code
2. Create and push a version tag

```bash
git add .
git commit -m "Update player feature"
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0
```

3. GitHub Actions `build-player` job runs on self-hosted runner:
   - Builds ARM64 binary
   - Computes SHA-256 and creates signed manifest JSON
   - Writes binary + manifest to network share (UpdatesPath)
   - Creates GitHub Release with tar.gz + manifest for cold-bootstrap reference
4. Server reconciles within 60 seconds (automatic) or immediately via admin "Sync now" button
5. Admin assigns version to device group via Versions page
6. Players automatically download new version on next heartbeat (~30 seconds)

### Manual Workflow Trigger

Navigate to GitHub Actions → Build and Deploy → Run workflow

## Self-Hosted Runner Setup

To enable automated deployment to your production server (macOS, Linux, or Windows with Docker Desktop):

### 1. Install Runner

The self-hosted runner for deployment can run on any platform with Docker installed. Here are examples for different platforms:

#### macOS (with Docker Desktop)

```bash
# Create runner directory
mkdir -p ~/actions-runner && cd ~/actions-runner

# Download latest runner
curl -o actions-runner-osx-x64-2.311.0.tar.gz \
  -L https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-osx-x64-2.311.0.tar.gz

# Extract
tar xzf ./actions-runner-osx-x64-2.311.0.tar.gz

# Configure
./config.sh --url https://github.com/glensouza/PlainSight --token YOUR_TOKEN

# Install as service
sudo ./svc.sh install
sudo ./svc.sh start
```

### 2. Configure Runner Permissions

Ensure the runner has access to:
- Docker socket
- PlainSight deployment directory
- Network access to pull images

### 3. Set Up File Share

Create a directory for the file share accessible by the runner:

```bash
mkdir -p /path/to/plainsight/file-share
```

Update the deployment script to reference this path.

## Monitoring Workflow Runs

### View Workflow Status

- GitHub Repository → Actions tab
- Click on specific workflow run for details
- View logs for each job and step

### Workflow Badges

Add to README.md:

```markdown
![Build Status](https://github.com/glensouza/PlainSight/workflows/Build%20and%20Deploy%20PlainSight/badge.svg)
```

## Troubleshooting

### Build Fails

**Check logs**:
1. Go to Actions tab
2. Click failing workflow run
3. Expand failed step
4. Review error messages

**Common issues**:
- Missing dependencies: Update Dockerfile
- Test failures: Fix tests before merging
- Docker build timeout: Optimize Dockerfile

### Image Push Fails

**Causes**:
- Insufficient permissions
- Network issues
- Registry quota exceeded

**Solution**:
```bash
# Check GitHub Token permissions
# Ensure workflow has packages: write permission
```

### Player Build Fails

**Causes**:
- .NET SDK version mismatch
- Missing project references
- Platform-specific dependencies
- Self-hosted runner offline

**Solution**:
```bash
# Verify .NET version in workflow matches project
# Check all project references are valid
# Test local build: dotnet publish -r linux-arm64
# Verify self-hosted runner is online and has network share access
```

### Manifest Signature Verification Fails

**Causes**:
- `secrets.PLAINSIGHT_SIGNING_KEY` not set or malformed
- Public key file (`src/PlainSight.Server/Keys/release-signing.pub`) missing or corrupted
- Manifest signed with wrong key

**Solution**:
```bash
# Check secrets.PLAINSIGHT_SIGNING_KEY is set and valid PKCS8 format
# Verify public key exists: cat src/PlainSight.Server/Keys/release-signing.pub
# Regenerate keypair if needed (see Key Pair Setup section above)
# Check server logs during reconciliation for signature errors
```

### Deployment Fails

**Causes**:
- Self-hosted runner offline
- Docker daemon not running
- Network connectivity issues

**Solution**:
```bash
# Check runner status
sudo ./svc.sh status

# Restart Docker
sudo systemctl restart docker

# Test manual deployment
docker compose pull plainsight-server
docker compose up -d plainsight-server
```

## Best Practices

### Version Tagging

Use semantic versioning:
- `v1.0.0` - Major release
- `v1.1.0` - Minor release (new features)
- `v1.1.1` - Patch release (bug fixes)

### Canary Deployments

1. Tag with new version: `v1.1.0`
2. Wait for server reconciliation (60s, or click "Sync now" on Versions page)
3. Assign new version to "canary" device group in admin panel
4. Monitor for issues (use Screenshot feature)
5. Assign version to larger device groups or "Default" group
6. Devices auto-update on next heartbeat cycle

### Testing Before Release

```bash
# Run tests locally
dotnet test

# Build Docker image locally
docker build -t plainsight-test -f src/PlainSight.Server/Dockerfile .

# Test player build locally
dotnet publish src/PlainSight.Player/PlainSight.Player.csproj \
  -r linux-arm64 --self-contained
```

### Rollback Procedure

If a deployment causes issues:

1. **Find previous image**:
```bash
docker images | grep plainsight-server
```

2. **Update docker-compose.yml**:
```yaml
services:
  plainsight-server:
    image: ghcr.io/glensouza/plainsight/plainsight-server:sha-abc1234
```

3. **Redeploy**:
```bash
docker compose up -d plainsight-server
```

## Extending the Workflow

### Add Testing Stage

```yaml
test:
  runs-on: macos-latest
  steps:
    - uses: actions/checkout@v4
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
    - name: Run tests
      run: dotnet test
```

### Add Notifications

```yaml
- name: Send notification
  if: failure()
  uses: 8398a7/action-slack@v3
  with:
    status: ${{ job.status }}
    webhook_url: ${{ secrets.SLACK_WEBHOOK }}
```

### Add Security Scanning

```yaml
- name: Run Trivy vulnerability scanner
  uses: aquasecurity/trivy-action@master
  with:
    image-ref: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest
    format: 'sarif'
    output: 'trivy-results.sarif'
```

## Conclusion

The GitHub Actions workflow provides:
- ✅ Automated builds
- ✅ Container image management
- ✅ Automated player releases
- ✅ Image retention for rollback
- ✅ Production deployment (with self-hosted runner)

For questions or issues, open a GitHub Issue or consult the Actions logs.
