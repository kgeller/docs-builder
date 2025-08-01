// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Documentation.Configuration;
using Elastic.Documentation.Extensions;
using Elastic.Markdown.Myst;
using Elastic.Markdown.Myst.Renderers.LlmMarkdown;
using Markdig.Renderers;
using Microsoft.Extensions.ObjectPool;

namespace Elastic.Markdown.Helpers;

internal static class DocumentationObjectPoolProvider
{
	private static readonly ObjectPoolProvider PoolProvider = new DefaultObjectPoolProvider();

	public static readonly ObjectPool<StringBuilder> StringBuilderPool = PoolProvider.CreateStringBuilderPool(256, 4 * 1024);
	public static readonly ObjectPool<ReusableStringWriter> StringWriterPool = PoolProvider.Create(new ReusableStringWriterPooledObjectPolicy());
	public static readonly ObjectPool<HtmlRenderSubscription> HtmlRendererPool = PoolProvider.Create(new HtmlRendererPooledObjectPolicy());
	private static readonly ObjectPool<LlmMarkdownRenderSubscription> LlmMarkdownRendererPool = PoolProvider.Create(new LlmMarkdownRendererPooledObjectPolicy());

	public static string UseLlmMarkdownRenderer<TContext>(BuildContext buildContext, TContext context, Action<LlmMarkdownRenderer, TContext> action)
	{
		var subscription = LlmMarkdownRendererPool.Get();
		subscription.SetBuildContext(buildContext);
		try
		{
			action(subscription.LlmMarkdownRenderer, context);
			return subscription.RentedStringBuilder!.ToString();
		}
		finally
		{
			LlmMarkdownRendererPool.Return(subscription);
		}
	}

	private sealed class ReusableStringWriterPooledObjectPolicy : IPooledObjectPolicy<ReusableStringWriter>
	{
		public ReusableStringWriter Create() => new();

		public bool Return(ReusableStringWriter obj)
		{
			obj.Reset();
			return true;
		}
	}

	public sealed class HtmlRenderSubscription
	{
		public required HtmlRenderer HtmlRenderer { get; init; }
		public StringBuilder? RentedStringBuilder { get; internal set; }
	}

	private sealed class HtmlRendererPooledObjectPolicy : IPooledObjectPolicy<HtmlRenderSubscription>
	{
		public HtmlRenderSubscription Create()
		{
			var stringBuilder = StringBuilderPool.Get();
			using var stringWriter = StringWriterPool.Get();
			stringWriter.SetStringBuilder(stringBuilder);
			var renderer = new HtmlRenderer(stringWriter);
			MarkdownParser.Pipeline.Setup(renderer);

			return new HtmlRenderSubscription { HtmlRenderer = renderer, RentedStringBuilder = stringBuilder };
		}

		public bool Return(HtmlRenderSubscription subscription)
		{
			//return string builder
			if (subscription.RentedStringBuilder is not null)
				StringBuilderPool.Return(subscription.RentedStringBuilder);

			subscription.RentedStringBuilder = null;

			var renderer = subscription.HtmlRenderer;

			//reset string writer
			((ReusableStringWriter)renderer.Writer).Reset();

			// reseed string writer with string builder
			var stringBuilder = StringBuilderPool.Get();
			subscription.RentedStringBuilder = stringBuilder;
			((ReusableStringWriter)renderer.Writer).SetStringBuilder(stringBuilder);
			return true;
		}
	}

	private sealed class LlmMarkdownRenderSubscription
	{
		public required LlmMarkdownRenderer LlmMarkdownRenderer { get; init; }
		public StringBuilder? RentedStringBuilder { get; internal set; }

		public void SetBuildContext(BuildContext buildContext) => LlmMarkdownRenderer.BuildContext = buildContext;
	}

	private sealed class LlmMarkdownRendererPooledObjectPolicy : IPooledObjectPolicy<LlmMarkdownRenderSubscription>
	{
		public LlmMarkdownRenderSubscription Create()
		{
			var stringBuilder = StringBuilderPool.Get();
			using var stringWriter = StringWriterPool.Get();
			stringWriter.SetStringBuilder(stringBuilder);
			var renderer = new LlmMarkdownRenderer(stringWriter)
			{
				BuildContext = null!
			};
			return new LlmMarkdownRenderSubscription { LlmMarkdownRenderer = renderer, RentedStringBuilder = stringBuilder };
		}

		public bool Return(LlmMarkdownRenderSubscription subscription)
		{
			//return string builder
			if (subscription.RentedStringBuilder is not null)
				StringBuilderPool.Return(subscription.RentedStringBuilder);

			subscription.RentedStringBuilder = null;

			var renderer = subscription.LlmMarkdownRenderer;

			//reset string writer
			((ReusableStringWriter)renderer.Writer).Reset();

			// reseed string writer with string builder
			var stringBuilder = StringBuilderPool.Get();
			subscription.RentedStringBuilder = stringBuilder;
			((ReusableStringWriter)renderer.Writer).SetStringBuilder(stringBuilder);

			// Reset BuildContext to null for reuse
			renderer.BuildContext = null!;

			return true;
		}
	}
}
