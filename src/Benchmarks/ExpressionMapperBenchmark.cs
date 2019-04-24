using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Columns;

namespace Benchmarks
{
    [RankColumn, CategoriesColumn, BenchmarkCategory("ExpressionMapper")]
    public class ExpressionMapperBenchmark : BenchmarkBase
    {
        [GlobalSetup]
        public override void Initial()
        {
            ExpressionMapper.ExpressionMapper.Map<TestA, TestB>(new TestA());
        }

        [Benchmark]
        public override void Nomal()
        {
            var model = GetNomalModel();
            var b = ExpressionMapper.ExpressionMapper.Map<TestA, TestB>(model);
        }

        [Benchmark]
        public override void Complex()
        {
            var model = GetComplexModel();
            var b = ExpressionMapper.ExpressionMapper.Map<TestA, TestB>(model);
        }

        [Benchmark]
        public override void Nest()
        {
            var model = GetNestModel();
            var b = ExpressionMapper.ExpressionMapper.Map<TestA, TestB>(model);
        }

        [Benchmark]
        public override void List()
        {
            var model = GetListModel();
            var b = ExpressionMapper.ExpressionMapper.Map<TestA, TestB>(model);
        }
    }
}