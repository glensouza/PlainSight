# PlainSight Deployment Guide

This guide covers deploying the PlainSight server using Docker Compose on macOS with Docker Desktop.

## Prerequisites

- macOS with Docker Desktop installed
- At least 2GB available RAM
- At least 10GB available disk space
- Network access for Docker image pulls

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/glensouza/PlainSight.git
cd PlainSight
```

### 2. Configure Environment Variables

Create a `.env` file in the project root (optional):

```bash
# PostgreSQL password
POSTGRES_PASSWORD=your_secure_password_here

# Server configuration
ASPNETCORE_ENVIRONMENT=Production
```

If you don't create a `.env` file, the default password `plainsight123` will be used.

### 3. Start the Services

```bash
docker compose up -d
```

This command will:
- Pull the PostgreSQL 17 image
- Build the Signage.Server Docker image
- Pull the Samba file share image
- Create volumes for persistent data
- Start all services

### 4. Verify Deployment

Check that all containers are running:

```bash
docker compose ps
```

You should see three services running:
- `plainsight-postgres`
- `plainsight-server`
- `plainsight-samba`

### 5. Access the Application

Open your browser and navigate to:
- **Admin Interface**: http://localhost:8080
- **Health Check**: http://localhost:8080/health

## Service Configuration

### PostgreSQL Database

- **Port**: 5432
- **Database**: signagedb
- **Username**: plainsight
- **Password**: Set via `POSTGRES_PASSWORD` env var
- **Data Volume**: `postgres_data`

### Signage Server

- **Port**: 8080
- **Environment**: Production
- **File Share Mount**: `/mnt/signage`

### Samba File Share

- **Ports**: 139, 445
- **Username**: pi
- **Password**: secure
- **Share Name**: signage
- **Share Path**: `/share`

## Updating the Server

### Pull Latest Changes

```bash
git pull origin main
docker compose pull
docker compose up -d
```

### Manual Image Build

```bash
docker compose build signage-server
docker compose up -d signage-server
```

## Rollback

Docker Compose doesn't automatically keep old images, but you can:

### Tag Before Updating

```bash
docker tag plainsight-server:latest plainsight-server:backup-$(date +%Y%m%d)
```

### List Available Images

```bash
docker images | grep plainsight-server
```

### Rollback to Previous Version

```bash
docker compose down
docker tag plainsight-server:backup-20260125 plainsight-server:latest
docker compose up -d
```

## Monitoring

### View Logs

```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f signage-server
docker compose logs -f postgres
```

### Resource Usage

```bash
docker stats
```

## Maintenance

### Backup Database

```bash
docker compose exec postgres pg_dump -U plainsight signagedb > backup-$(date +%Y%m%d).sql
```

### Restore Database

```bash
cat backup-20260125.sql | docker compose exec -T postgres psql -U plainsight signagedb
```

### Clean Up Old Data

```bash
# Remove unused images
docker image prune -a

# Remove unused volumes (WARNING: This deletes data)
docker volume prune
```

## Troubleshooting

### Container Won't Start

```bash
# Check logs
docker compose logs signage-server

# Restart service
docker compose restart signage-server
```

### Database Connection Issues

```bash
# Check PostgreSQL is healthy
docker compose exec postgres pg_isready -U plainsight

# Reset database container
docker compose down
docker volume rm plainsight_postgres_data
docker compose up -d
```

### Port Conflicts

If ports 8080, 5432, 139, or 445 are already in use, modify `docker-compose.yml`:

```yaml
ports:
  - "8081:8080"  # Use different external port
```

## Security Considerations

1. **Change Default Passwords**: Update `POSTGRES_PASSWORD` in production
2. **Use HTTPS**: Configure reverse proxy (nginx, Traefik) for TLS
3. **Firewall Rules**: Restrict access to ports 5432, 139, 445
4. **Regular Updates**: Keep Docker images updated
5. **Backup Strategy**: Implement automated backups

## Performance Tuning

### PostgreSQL Configuration

Create `postgres.conf` and mount it:

```yaml
volumes:
  - ./postgres.conf:/etc/postgresql/postgresql.conf
```

### Resource Limits

Add to `docker-compose.yml`:

```yaml
services:
  signage-server:
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G
```

## Production Checklist

- [ ] Change default PostgreSQL password
- [ ] Configure automated backups
- [ ] Set up monitoring and alerts
- [ ] Configure HTTPS/TLS
- [ ] Review security settings
- [ ] Test rollback procedure
- [ ] Document custom configurations
- [ ] Set up log aggregation
