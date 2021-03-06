using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Divergic.Logging.Xunit;
using Microsoft.Extensions.Logging;
using Proto.Remote.Tests.Messages;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable MethodHasAsyncOverload

namespace Proto.Remote.Tests
{
    [Trait("Category", "Remote")]
    public class RemoteTests
    {
        public RemoteTests(ITestOutputHelper testOutputHelper)
        {
            var factory = LogFactory.Create(testOutputHelper);
            Log.SetLoggerFactory(factory);
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanSerializeAndDeserializeJsonPid()
        {
            await RemoteManager.EnsureRemote();
            var serialization = new Serialization();
            const string typeName = "actor.PID";
            var json = new JsonMessage(typeName, "{ \"Address\":\"123\", \"Id\":\"456\"}");
            var bytes = serialization.Serialize(json, 1);
            var deserialized = serialization.Deserialize(typeName, bytes, 1) as PID;
            Assert.NotNull(deserialized);
            Assert.Equal("123", deserialized.Address);
            Assert.Equal("456", deserialized.Id);
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanSerializeAndDeserializeJson()
        {
            await RemoteManager.EnsureRemote();
            var serialization = new Serialization();
            serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
            const string typeName = "remote_test_messages.Ping";
            var json = new JsonMessage(typeName, "{ \"message\":\"Hello\"}");
            var bytes = serialization.Serialize(json, 1);
            var deserialized = serialization.Deserialize(typeName, bytes, 1) as Ping;
            Assert.NotNull(deserialized);
            Assert.Equal("Hello", deserialized.Message);
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanSendJsonAndReceiveToExistingRemote()
        {
            var (remote, system) = await RemoteManager.EnsureRemote();
            var remoteActor = new PID(RemoteManager.RemoteAddress, "EchoActorInstance");
            var tcs = new TaskCompletionSource<bool>();

            var localActor = system.Root.Spawn(
                Props.FromFunc(
                    ctx =>
                    {
                        if (ctx.Message is Pong)
                        {
                            tcs.SetResult(true);
                            ctx.Stop(ctx.Self!);
                        }

                        return Task.CompletedTask;
                    }
                )
            );

            var json = new JsonMessage("remote_test_messages.Ping", "{ \"message\":\"Hello\"}");
            var envelope = new Proto.MessageEnvelope(json, localActor, Proto.MessageHeader.Empty);
            remote.SendMessage(remoteActor, envelope, 1);
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanSendAndReceiveToExistingRemote()
        {
            var (_, system) = await RemoteManager.EnsureRemote();

            var remoteActor = new PID(RemoteManager.RemoteAddress, "EchoActorInstance");

            var pong = await system.Root.RequestAsync<Pong>(remoteActor, new Ping {Message = "Hello"},
                TimeSpan.FromMilliseconds(5000)
            );

            Assert.Equal($"{RemoteManager.RemoteAddress} Hello", pong.Message);

            // await service.StopAsync();
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task WhenRemoteActorNotFound_RequestAsyncTimesOut()
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var unknownRemoteActor = new PID(RemoteManager.RemoteAddress, "doesn't exist");

            await Assert.ThrowsAsync<TimeoutException>(
                async () =>
                {
                    await system.Root.RequestAsync<Pong>(unknownRemoteActor, new Ping {Message = "Hello"},
                        TimeSpan.FromMilliseconds(2000)
                    );
                }
            );
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanSpawnRemoteActor()
        {
            var (remote, system) = await RemoteManager.EnsureRemote();
            var remoteActorName = Guid.NewGuid().ToString();

            var remoteActorResp = await remote.SpawnNamedAsync(
                RemoteManager.RemoteAddress, remoteActorName, "EchoActor", TimeSpan.FromSeconds(5)
            );
            var remoteActor = remoteActorResp.Pid;
            var pong = await system.Root.RequestAsync<Pong>(remoteActor, new Ping {Message = "Hello"},
                TimeSpan.FromMilliseconds(5000)
            );
            Assert.Equal($"{RemoteManager.RemoteAddress} Hello", pong.Message);
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanWatchRemoteActor()
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var remoteActor = await SpawnRemoteActor(RemoteManager.RemoteAddress);
            var localActor = await SpawnLocalActorAndWatch(remoteActor);

            system.Root.Stop(remoteActor);

            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(
                            localActor, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanWatchMultipleRemoteActors()
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var remoteActor1 = await SpawnRemoteActor(RemoteManager.RemoteAddress);
            var remoteActor2 = await SpawnRemoteActor(RemoteManager.RemoteAddress);
            var localActor = await SpawnLocalActorAndWatch(remoteActor1, remoteActor2);

            system.Root.Stop(remoteActor1);
            system.Root.Stop(remoteActor2);

            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(
                            localActor, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor1.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );

            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(
                            localActor, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor2.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task MultipleLocalActorsCanWatchRemoteActor()
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var remoteActor = await SpawnRemoteActor(RemoteManager.RemoteAddress);

            var localActor1 = await SpawnLocalActorAndWatch(remoteActor);
            var localActor2 = await SpawnLocalActorAndWatch(remoteActor);
            system.Root.Stop(remoteActor);

            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(
                            localActor1, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );

            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(
                            localActor2, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task CanUnwatchRemoteActor()
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var remoteActor = await SpawnRemoteActor(RemoteManager.RemoteAddress);
            var localActor1 = await SpawnLocalActorAndWatch(remoteActor);
            var localActor2 = await SpawnLocalActorAndWatch(remoteActor);
            system.Root.Send(localActor2, new Unwatch(remoteActor));
            await Task.Delay(TimeSpan.FromSeconds(3)); // wait for unwatch to propagate...
            system.Root.Stop(remoteActor);

            // localActor1 is still watching so should get notified
            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(
                            localActor1, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );

            // localActor2 is NOT watching so should not get notified
            Assert.False(
                await system.Root.RequestAsync<bool>(
                    localActor2, new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor.Id),
                    TimeSpan.FromSeconds(5)
                ),
                "Unwatch did not succeed."
            );
        }

        [Fact]
        [DisplayTestMethodName]
        public async Task WhenRemoteTerminated_LocalWatcherReceivesNotification()
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var remoteActor = await SpawnRemoteActor(RemoteManager.RemoteAddress);
            var localActor = await SpawnLocalActorAndWatch(remoteActor);

            system.Root.Send(remoteActor, new Die());
            Assert.True(
                await PollUntilTrue(
                    () =>
                        system.Root.RequestAsync<bool>(localActor,
                            new TerminatedMessageReceived(RemoteManager.RemoteAddress, remoteActor.Id),
                            TimeSpan.FromSeconds(5)
                        )
                ),
                "Watching actor did not receive Termination message"
            );
            Assert.Equal(1,
                await system.Root.RequestAsync<int>(localActor, new GetTerminatedMessagesCount(),
                    TimeSpan.FromSeconds(5)
                )
            );
        }

        private async Task<PID> SpawnRemoteActor(string address)
        {
            var (remote, _) = await RemoteManager.EnsureRemote();
            var remoteActorName = Guid.NewGuid().ToString();
            var remoteActorResp =
                await remote.SpawnNamedAsync(address, remoteActorName, "EchoActor", TimeSpan.FromSeconds(5));
            return remoteActorResp.Pid;
        }

        private async Task<PID> SpawnLocalActorAndWatch(params PID[] remoteActors)
        {
            var (_, system) = await RemoteManager.EnsureRemote();
            var props = Props.FromProducer(() => new LocalActor(remoteActors));
            var actor = system.Root.Spawn(props);

            // The local actor watches the remote one - we wait here for the RemoteWatch 
            // message to propagate to the remote actor
            Console.WriteLine("Waiting for RemoteWatch to propagate...");
            await Task.Delay(2000);
            return actor;
        }

        private Task<bool> PollUntilTrue(Func<Task<bool>> predicate)
        {
            return PollUntilTrue(predicate, 10, TimeSpan.FromMilliseconds(500));
        }

        private async Task<bool> PollUntilTrue(Func<Task<bool>> predicate, int attempts, TimeSpan interval)
        {
            var logger = Log.CreateLogger(nameof(PollUntilTrue));
            var attempt = 1;

            while (attempt <= attempts)
            {
                logger.LogInformation($"Attempting assertion (attempt {attempt} of {attempts})");

                if (await predicate())
                {
                    logger.LogInformation("Passed!");
                    return true;
                }

                attempt++;
                await Task.Delay(interval);
            }

            return false;
        }
    }

    public class TerminatedMessageReceived
    {
        public TerminatedMessageReceived(string address, string actorId)
        {
            Address = address;
            ActorId = actorId;
        }

        public string Address { get; }
        public string ActorId { get; }
    }

    public class GetTerminatedMessagesCount
    {
    }

    public class LocalActor : IActor
    {
        private readonly List<PID> _remoteActors = new List<PID>();
        private readonly List<Terminated> _terminatedMessages = new List<Terminated>();

        public LocalActor(params PID[] remoteActors)
        {
            _remoteActors.AddRange(remoteActors);
        }

        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Started _:
                    HandleStarted(context);
                    break;
                case Unwatch msg:
                    HandleUnwatch(context, msg);
                    break;
                case TerminatedMessageReceived msg:
                    HandleTerminatedMessageReceived(context, msg);
                    break;
                case GetTerminatedMessagesCount _:
                    HandleCountOfMessagesReceived(context);
                    break;
                case Terminated msg:
                    HandleTerminated(msg);
                    break;
            }

            return Task.CompletedTask;
        }

        private void HandleCountOfMessagesReceived(IContext context)
        {
            context.Respond(_terminatedMessages.Count);
        }

        private void HandleTerminatedMessageReceived(IContext context, TerminatedMessageReceived msg)
        {
            var messageReceived = _terminatedMessages.Any(
                tm => tm.Who.Address == msg.Address &&
                      tm.Who.Id == msg.ActorId
            );
            context.Respond(messageReceived);
        }

        private void HandleTerminated(Terminated msg)
        {
            Console.WriteLine(
                $"Received Terminated message for {msg.Who.Address}: {msg.Who.Id}. Address terminated? {msg.AddressTerminated}"
            );
            _terminatedMessages.Add(msg);
        }

        private void HandleUnwatch(IContext context, Unwatch msg)
        {
            var remoteActor = _remoteActors.Single(
                ra => ra.Id == msg.Watcher.Id &&
                      ra.Address == msg.Watcher.Address
            );

            context.Unwatch(remoteActor);
        }

        private void HandleStarted(IContext context)
        {
            foreach (var remoteActor in _remoteActors) context.Watch(remoteActor);
        }
    }
}