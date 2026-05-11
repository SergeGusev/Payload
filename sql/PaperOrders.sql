 select
      po.id as paper_order_id,
      po.created_at_utc,
      po.status,
      po.copied_trader_wallet,
      po.asset_id,
      po.condition_id,
      po.outcome as selected_outcome,
      po.price,
      po.size_shares,
      po.notional_usd,
      case
          when po.status not in ('Filled', 'PartiallyFilled') then po.status
          when ps.id is null then 'OPEN_OR_NOT_SETTLED'
          when ps.won then 'WON'
          else 'LOST'
      end as result,
      ps.winning_outcome,
      ps.settlement_value_usd,
      ps.realized_pnl_usd,
      ps.settled_at_utc
  from public.paper_orders po
  left join public.paper_position_settlements ps
      on lower(ps.copied_trader_wallet) = lower(po.copied_trader_wallet)
     and ps.asset_id = po.asset_id
	 where strategy_id = 'b7c50005-0000-4000-8001-000000000180'
  order by po.created_at_utc desc;
	
	
	
	