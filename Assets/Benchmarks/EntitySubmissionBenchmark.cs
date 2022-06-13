using NUnit.Framework;
using Svelto.ECS;
using Svelto.ECS.Schedulers;
using Unity.PerformanceTesting;

namespace Tests
{
    // WarmupCount(int n) - number of times to to execute before measurements are collected. If unspecified, a default warmup is executed. This default warmup will wait for 100 ms. However, if less than 3 method executions have finished in that time, the warmup will wait until 3 method executions have completed.
    // MeasurementCount(int n) - number of measurements to capture. If not specified default value is 9.
    // IterationsPerMeasurement(int n) - number of method executions per measurement to use. If this value is not specified, the method will be executed as many times as possible until approximately 100 ms has elapsed.
    // SampleGroup(string name) - by default the measurement name will be "Time", this allows you to override it
    // GC() - if specified, will measure the total number of Garbage Collection allocation calls.
    // SetUp(Action action) - is called every iteration before executing the method. Setup time is not measured.
    // CleanUp(Action action) - is called every iteration after the execution of the method. Cleanup time is not measured
    
    public class EntitySubmissionBenchmark
    {
        string[] markers =
        {
            "Add operations"
        };
        
        [Test, Performance]
        public void TestEntitySubmissionPerformance()
        {
            SimpleEntitiesSubmissionScheduler scheduler = new SimpleEntitiesSubmissionScheduler();
            
            EnginesRoot    enginesRoot = default;
            IEntityFactory entityFactory = default;
            
            Measure.Method(() =>
            {
                using (Measure.Scope("add 1000 empty entities"))
                {
                    for (uint i = 0; i < 1000; i++)
                        entityFactory.BuildEntity<EmptyEntityDescriptor>(i, TestGroups.Group);
                }

                using (Measure.ProfilerMarkers(markers))
                {
                    using (Measure.Scope("submit 1000 empty entities"))
                    {
                        scheduler.SubmitEntities();
                    }
                }
            }).WarmupCount(5).MeasurementCount(10).SetUp(() =>
                {
                    enginesRoot   = new EnginesRoot(scheduler);
                    entityFactory = enginesRoot.GenerateEntityFactory();
                    
                    entityFactory.PreallocateEntitySpace<EmptyEntityDescriptor>(TestGroups.Group, 1000);
                    enginesRoot.Dispose();
                }
                ).Run();
            
            Measure.Method(() =>
            {
                using (Measure.Scope("add 1000 empty entities over 10 groups"))
                {
                    for (uint i = 0; i < 1000; i++)
                        entityFactory.BuildEntity<EmptyEntityDescriptor>(i, TestGroups.Group + i % 10);
                }
                
                using (Measure.Scope("Add 1000 empty entities over 10 groups"))
                {
                    scheduler.SubmitEntities(); 
                }
            }).WarmupCount(5).MeasurementCount(10).SetUp(() =>
                {
                    enginesRoot   = new EnginesRoot(scheduler);
                    entityFactory = enginesRoot.GenerateEntityFactory();
                    
                    for (int i = 0; i < 10; i++)
                        entityFactory.PreallocateEntitySpace<EmptyEntityDescriptor>(TestGroups.Group + (uint) i, 100);
                    enginesRoot.Dispose();                    
                }
            ).Run();
        }


        readonly SampleGroup[] submissionSampleGroups =
        {
            new SampleGroup("Add operations", SampleUnit.Millisecond),
            new SampleGroup("Swap Entities", SampleUnit.Millisecond),
            new SampleGroup("Swap Between Persistent Filters", SampleUnit.Millisecond),
        };

        [Test, Performance]
        public void TestEntitySubmissionFilteredHeavy([Range(1000, 10000, 1000)] int max)
        {
            SimpleEntitiesSubmissionScheduler scheduler = new SimpleEntitiesSubmissionScheduler();

            EnginesRoot      enginesRoot     = default;
            IEntityFactory   entityFactory   = default;
            IEntityFunctions entityFunctions = default;

            var eng = new EntitiesDBAccessEngine();

            Measure.Method(() =>
            {
                using (Measure.Scope($"Add {max} large entities across 10 groups"))
                {
                    for (uint i = 0; i < max; i++)
                    {
                        entityFactory.BuildEntity<PopulatedEntityDescriptor>(i, TestGroups.Group + i % 10);
                    }
                }

                using (Measure.ProfilerMarkers(submissionSampleGroups))
                {
                    using (Measure.Scope("submit 1000 empty entities"))
                    {
                        scheduler.SubmitEntities();
                    }
                }
                
                using (Measure.Scope("Adding varying dense filters to entities"))
                {
                    var filters = eng.entitiesDB.GetFilters();

                    for (var groupIndx = 0; groupIndx < 10; groupIndx++)
                    {
                        var group = (ExclusiveGroupStruct) (TestGroups.Group + (uint) groupIndx);

                        var filterA =
                            filters.GetOrCreatePersistentFilter<TestStruct1>(1,
                                EntitiesDBAccessEngine.contextID);
                        var filterB =
                            filters.GetOrCreatePersistentFilter<TestStruct1>(2,
                                EntitiesDBAccessEngine.contextID);
                        var filterC =
                            filters.GetOrCreatePersistentFilter<TestStruct1>(3,
                                EntitiesDBAccessEngine.contextID);

                        for (uint i = 0; i < max; i++)
                        {
                            if ((i + 1) % 2 == 0)
                                filterA.Add(new EGID(i, group), i);

                            if ((i + 1) % 4 == 0)
                                filterB.Add(new EGID(i, group), i);

                            if ((i + 1) % 8 == 0)
                                filterC.Add(new EGID(i, group), i);
                        }
                    }
                }

                using (Measure.Scope($"Enqueuing {max/5} group swap operations"))
                {
                    var fromGroup = TestGroups.Group;
                    var toGroup   = TestGroups.Group + 4;

                    for (var i = 0; i < max/5; i++)
                    {
                        var entityID = (max / (max/5)) * i;

                        entityFunctions.SwapEntityGroup<PopulatedEntityDescriptor>(new EGID((uint) entityID, fromGroup),
                            toGroup);
                    }
                }

                using (Measure.ProfilerMarkers(submissionSampleGroups))
                {
                    using (Measure.Scope("Submission"))
                        scheduler.SubmitEntities();
                }


            }).WarmupCount(5).MeasurementCount(10).SetUp(() =>
            {
                enginesRoot = new EnginesRoot(scheduler);
                entityFactory = enginesRoot.GenerateEntityFactory();
                entityFunctions = enginesRoot.GenerateEntityFunctions();

                for(uint groupIndx = 0; groupIndx < 10; groupIndx++)
                    entityFactory.PreallocateEntitySpace<PopulatedEntityDescriptor>(TestGroups.Group + groupIndx, 10000);

                enginesRoot.AddEngine(eng);
            }).CleanUp((() =>
            {
                enginesRoot.Dispose();
            })).Run();

        }
        
    }

    public class TestGroups
    {
        public static ExclusiveGroup Group = new ExclusiveGroup(10);
    }

    public class EmptyEntityDescriptor: GenericEntityDescriptor<TestStruct> { }

    public struct TestStruct : IEntityComponent { }

    public class EntitiesDBAccessEngine : IQueryingEntitiesEngine
    {
        public static FilterContextID contextID = FilterContextID.GetNewContextID();
        
        public void Ready()
        {}

        public EntitiesDB entitiesDB { get; set; }
    }
    
    public class PopulatedEntityDescriptor : IEntityDescriptor
    {
        public IComponentBuilder[] componentsToBuild => _componentBuilders;

        static IComponentBuilder[] _componentBuilders;
        
        static PopulatedEntityDescriptor()
        {
            _componentBuilders = new IComponentBuilder[]
            {
                new ComponentBuilder<TestStruct1>(),
                new ComponentBuilder<TestStruct2>(),
                new ComponentBuilder<TestStruct3>(),
                new ComponentBuilder<TestStruct4>(),
                new ComponentBuilder<TestStruct5>(),
                new ComponentBuilder<TestStruct6>(),
                new ComponentBuilder<TestStruct7>(),
                new ComponentBuilder<TestStruct8>(),
                new ComponentBuilder<TestStruct9>(),
                new ComponentBuilder<TestStruct10>(),
            };
        }
    }

    public struct TestStruct1 : IEntityComponent
    {
        public float a;
        public byte b;
    }

    public struct TestStruct2 : IEntityComponent
    {
        public uint a;
        public uint b;
        public uint c;
    }

    public struct TestStruct3 : IEntityComponent
    {
        public byte a;
        public float b;
    }

    public struct TestStruct4 : IEntityComponent
    {
        public float a;
        public float b;
    }
    
    public struct TestStruct5 : IEntityComponent
    {
        public float a;
        public byte b;
    }

    public struct TestStruct6 : IEntityComponent
    {
        public uint a;
        public uint b;
        public uint c;
    }

    public struct TestStruct7 : IEntityComponent
    {
        public byte a;
        public float b;
    }

    public struct TestStruct8 : IEntityComponent
    {
        public float a;
        public float b;
    }
    
    public struct TestStruct9 : IEntityComponent
    {
        public byte a;
        public float b;
    }

    public struct TestStruct10 : IEntityComponent
    {
        public float a;
        public float b;
    }
}