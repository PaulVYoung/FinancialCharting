﻿#region Usings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using FinancialCharting.Library.Configuration;
using FinancialCharting.Library.Logging;
using FinancialCharting.Library.Models;
using FinancialCharting.Library.Models.Common;
using FinancialCharting.Library.Models.MarketData.Interfaces;
using FinancialCharting.Library.Models.QuandlJsonModels;
using FinancialCharting.Library.ProjectExceptions;
using FinancialCharting.Library.Quandl.Interfaces;

using HtmlAgilityPack;

using RestSharp;

using ServiceStack.Text;

#endregion

namespace FinancialCharting.Library.Quandl
{
	public class QuandlProvider : IQuandlProvider
	{
		private readonly ILogWrapper _logger;
		private readonly IQuandlMapper _mapper;

		public QuandlProvider(ILogWrapper logger, IQuandlMapper mapper)
		{
			_logger = logger;
			_mapper = mapper;
		}

		public OperationResult<List<DataSource>> GetFinancialDataSources()
		{
			var result = QuandlSettings.DataSources;

			if (result.Any())
			{
				return new OperationResult<List<DataSource>>(result);
			}
			else
			{
				return new OperationResult<List<DataSource>>(false, "No data found");
			}
		}

		public OperationResult<List<DataSource>> GetAllFinancialDataSources()
		{
			try
			{
				var web = new HtmlWeb {AutoDetectEncoding = false, OverrideEncoding = Encoding.UTF8,};
				var doc = web.Load("https://www.quandl.com/resources/data-sources");

				var node = doc.GetElementbyId("Financial-Data");

				if (node != null)
				{
					var table = node.NextSibling.NextSibling.ChildNodes.Nodes();
					var list = new List<DataSource>();

					foreach (var row in table)
					{
						if (row.Name == "tr" && row.ParentNode.Name != "thead")
						{
							var dataSource = new DataSource();

							var splitted = row.InnerText.Split(new[] {"\n"}, StringSplitOptions.RemoveEmptyEntries);
							dataSource.Name = splitted[0];
							dataSource.Count = Convert.ToInt32(splitted[1].Replace(",", ""));
							dataSource.Description = splitted[2];
							dataSource.Code = splitted[4];

							list.Add(dataSource);
						}
					}

					if (list.Any())
					{
						return new OperationResult<List<DataSource>>(list);
					}
					else
					{
						return new OperationResult<List<DataSource>>(false, "No data found");
					}
				}
				else
				{
					return new OperationResult<List<DataSource>>(false, "Can't load data sources list from web site");
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex);
				throw new QuandlProviderException(ex);
			}
		}

		public OperationResult<List<IMarketData>> GetMarketData(QuandlMarketDataRequest request)
		{
			try
			{
				var client = new RestClient(QuandlSettings.QuandlUrl + request.DataSource + "/");
				var restRequest = new RestRequest(request.Ticker + ".json", Method.GET);
				restRequest.AddParameter("sort_order", request.SortOrder.ToString().ToLower());
				restRequest.AddParameter("exclude_headers", request.ExcludeHeaders);

				if (request.RowsNumber.HasValue)
				{
					restRequest.AddParameter("rows", request.RowsNumber);
				}
				else
				{
					restRequest.AddParameter("trim_start", request.From);
					restRequest.AddParameter("trim_end", request.To);
				}

				if (request.SpecificColumnNumber.HasValue)
				{
					restRequest.AddParameter("column", request.SpecificColumnNumber);
				}

				restRequest.AddParameter("collapse", request.Timeframe.ToString().ToLower());
				restRequest.AddParameter("transformation", request.Transformation.ToString().ToLower());
				restRequest.AddParameter("auth_token", QuandlSettings.QuandlToken);

				var response = client.Execute(restRequest);

				if (response.ResponseStatus != ResponseStatus.Completed)
				{
					_logger.Error(response.ErrorException);
					throw new QuandlProviderException(response.ErrorMessage, response.ErrorException);
				}
				else
				{
					var actual = response.Content.FromJson<RootObjectMarketData>();
					if (actual.data.Any() && actual.column_names.Any())
					{
						var list = new List<IMarketData>();

						foreach (var data in actual.data)
						{
							var result = _mapper.ToMarketData(request.DataSource, data);
							list.Add(result);
						}

						if (list.Any())
						{
							return new OperationResult<List<IMarketData>>(list);
						}
						else
						{
							return new OperationResult<List<IMarketData>>(false, "No data found");
						}
					}
					else
					{
						return new OperationResult<List<IMarketData>>(false, "No market data found for current ticker");
					}
				}
			}
			catch (NotSupportedException ex)
			{
				_logger.Error(ex);
				throw new QuandlProviderException("This datasource is not supported", ex);
			}
			catch (ParsingException ex)
			{
				_logger.Error(ex);
				throw new QuandlProviderException(ex.Message, ex);
			}
			catch (Exception ex)
			{
				_logger.Error(ex);
				throw new QuandlProviderException(ex);
			}
		}

		public OperationResult<DataSet> GetTickers(string query, bool isSearch, PagingOptions paging)
		{
			try
			{
				var client = new RestClient(QuandlSettings.QuandlSearchUrl);
				var restRequest = new RestRequest("datasets.json", Method.GET);

				if (isSearch)
				{
					restRequest.AddParameter("query", query);
				}
				else
				{
					restRequest.AddParameter("query", "*");
					restRequest.AddParameter("source_code", query);
				}

				restRequest.AddParameter("per_page", paging.PerPage);
				restRequest.AddParameter("page", paging.PageNumber);
				restRequest.AddParameter("auth_token", QuandlSettings.QuandlToken);

				var response = client.Execute(restRequest);

				if (response.ResponseStatus != ResponseStatus.Completed)
				{
					_logger.Error(response.ErrorException);
					return new OperationResult<DataSet>(false, response.ErrorMessage);
				}
				else
				{
					var result = response.Content.FromJson<RootObjectDataSet>();
					var data = _mapper.ToDataSet(result);

					if (isSearch)
					{
						data.TotalCount = data.Tickers.Count;
					}

					return new OperationResult<DataSet>(data);
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex);
				throw new QuandlProviderException(ex);
			}
		}
	}
}
