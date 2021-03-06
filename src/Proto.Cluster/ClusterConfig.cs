﻿// -----------------------------------------------------------------------
//   <copyright file="Cluster.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Proto.Cluster.IdentityLookup;
using Proto.Remote;

namespace Proto.Cluster
{
    [PublicAPI]
    public class ClusterConfig
    {
        public ClusterConfig(string name, string host, int port, IClusterProvider cp)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            host = host ?? throw new ArgumentNullException(nameof(host));
            ClusterProvider = cp ?? throw new ArgumentNullException(nameof(cp));

            RemoteConfig = new RemoteConfig(host, port);
            TimeoutTimespan = TimeSpan.FromSeconds(5);
            HeartBeatInterval = TimeSpan.FromSeconds(30);
            MemberStrategyBuilder = kind => new SimpleMemberStrategy();
            ClusterKinds = new Dictionary<string, Props>();
        }

        public string Name { get; }
        
        public Dictionary<string, Props> ClusterKinds { get; } 

        public IClusterProvider ClusterProvider { get; }

        public RemoteConfig RemoteConfig { get; private set; }
        public TimeSpan TimeoutTimespan { get; private set; }

        public Func<string, IMemberStrategy> MemberStrategyBuilder { get; private set; }

        public bool ClusterClient { get; set; }

        public IIdentityLookup? IdentityLookup { get; private set; }
        public TimeSpan HeartBeatInterval { get; set; }

        public ClusterConfig WithTimeoutSeconds(int timeoutSeconds)
        {
            TimeoutTimespan = TimeSpan.FromSeconds(timeoutSeconds);
            return this;
        }

        public ClusterConfig WithMemberStrategyBuilder(Func<string, IMemberStrategy> builder)
        {
            MemberStrategyBuilder = builder;
            return this;
        }

        public ClusterConfig WithIdentityLookup(IIdentityLookup identityLookup)
        {
            IdentityLookup = identityLookup;
            return this;
        }
        
        public ClusterConfig WithRemoteConfig(RemoteConfig remoteConfig)
        {
            RemoteConfig = remoteConfig;
            return this;
        }

        public ClusterConfig WithRemoteConfig(Action<RemoteConfig> remoteConfigurator)
        {
            remoteConfigurator(RemoteConfig);
            return this;
        }
        
        public ClusterConfig WithClusterKind(string kind, Props prop)
        {
            ClusterKinds.Add(kind, prop);
            return this;
        }

        public ClusterConfig WithClusterKinds(params (string kind, Props prop)[] knownKinds)
        {
            foreach (var (kind, prop) in knownKinds) ClusterKinds.Add(kind, prop);
            return this;
        }
    }
}