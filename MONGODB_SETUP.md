# MongoDB Setup for W3Champions

This document describes how to set up MongoDB for W3Champions website-backend.

## Transaction Support

The W3Champions website-backend requires MongoDB transaction support, which is only available when MongoDB is running as a replica set (not in standalone mode).

In order to do that, attach `--replSet rs0` to the launch command of the MongoDB.
Once started, initiate the replicate set with the following command:
```javascript
rs.initiate()
```

### Connection String

The service expects the following connection string format:

- Replica Set: `mongodb://hostname:port/?replicaSet=rs0`

If a replica set name is not provided in the connection string, the service will fail to start.

## Troubleshooting

If you encounter the error `System.NotSupportedException: Standalone servers do not support transactions`, it means:

1. MongoDB is running in standalone mode
2. You need to manually configure MongoDB as a replica set
