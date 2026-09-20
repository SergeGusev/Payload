namespace PolyCopyTrader.Storage;

public static class PostgresPaperConfirmationProjectionSchemaMigration
{
    public const string Id = "0014-paper-confirmation-projection";
    public const string Sql = """
        CREATE TABLE paper_confirmation_projection_cursor (
            kind text PRIMARY KEY, cursor_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
            completed boolean NOT NULL DEFAULT false);
        INSERT INTO paper_confirmation_projection_cursor(kind) VALUES ('O'),('R'),('S'),('F');
        CREATE TABLE paper_confirmation_projection_queue (sequence_id bigserial PRIMARY KEY, kind text NOT NULL, id uuid NOT NULL,
            observed_confirmed boolean);
        CREATE TABLE paper_confirmation_projection_members (
            kind text NOT NULL, id uuid NOT NULL, hours integer NOT NULL, strategy_id uuid NOT NULL,
            expires_at timestamptz, records bigint NOT NULL, closed bigint NOT NULL, confirmed bigint NOT NULL,
            net numeric NOT NULL, denominator numeric NOT NULL, missing_fee bigint NOT NULL,
            corrected bigint NOT NULL, deferred bigint NOT NULL, pending boolean NOT NULL DEFAULT false, PRIMARY KEY(kind,id,hours));
        CREATE INDEX ix_paper_confirmation_projection_expiry ON paper_confirmation_projection_members(expires_at)
            WHERE expires_at IS NOT NULL;
        CREATE TABLE paper_confirmation_projection_totals (
            strategy_id uuid NOT NULL, kind text NOT NULL, hours integer NOT NULL,
            records bigint NOT NULL DEFAULT 0, closed bigint NOT NULL DEFAULT 0, confirmed bigint NOT NULL DEFAULT 0,
            net numeric NOT NULL DEFAULT 0, denominator numeric NOT NULL DEFAULT 0,
            missing_fee bigint NOT NULL DEFAULT 0, corrected bigint NOT NULL DEFAULT 0, deferred bigint NOT NULL DEFAULT 0,
            PRIMARY KEY(strategy_id,kind,hours));
        CREATE TABLE paper_confirmation_projection_state (
            singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton), refreshed_at timestamptz,
            initialized boolean NOT NULL DEFAULT false, arrivals bigint NOT NULL DEFAULT 0,
            unique_confirmations bigint NOT NULL DEFAULT 0);
        INSERT INTO paper_confirmation_projection_state DEFAULT VALUES;
        CREATE TABLE paper_confirmation_projection_seen (id uuid PRIMARY KEY, confirmed boolean NOT NULL DEFAULT false);

        CREATE FUNCTION paper_confirmation_projection_delta() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE r paper_confirmation_projection_members; direction integer;
        BEGIN
            IF TG_OP='DELETE' THEN r:=OLD; direction:=-1; ELSE r:=NEW; direction:=1; END IF;
            INSERT INTO paper_confirmation_projection_totals AS t
                (strategy_id,kind,hours,records,closed,confirmed,net,denominator,missing_fee,corrected,deferred)
            VALUES(r.strategy_id,r.kind,r.hours,direction*r.records,direction*r.closed,direction*r.confirmed,
                direction*r.net,direction*r.denominator,direction*r.missing_fee,direction*r.corrected,direction*r.deferred)
            ON CONFLICT(strategy_id,kind,hours) DO UPDATE SET
                records=t.records+excluded.records,closed=t.closed+excluded.closed,confirmed=t.confirmed+excluded.confirmed,
                net=t.net+excluded.net,denominator=t.denominator+excluded.denominator,
                missing_fee=t.missing_fee+excluded.missing_fee,corrected=t.corrected+excluded.corrected,deferred=t.deferred+excluded.deferred;
            RETURN NULL;
        END $$;
        CREATE TRIGGER paper_confirmation_projection_delta AFTER INSERT OR DELETE ON paper_confirmation_projection_members
            FOR EACH ROW EXECUTE FUNCTION paper_confirmation_projection_delta();

        CREATE FUNCTION paper_confirmation_projection_enqueue() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE row_id uuid; wallet text; asset text;
        BEGIN
            row_id:=CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
            IF TG_ARGV[0]='O' THEN
                INSERT INTO paper_confirmation_projection_queue(kind,id,observed_confirmed)
                    VALUES('O',row_id,CASE WHEN TG_OP='DELETE' THEN OLD.confirmed ELSE NEW.confirmed END);
            ELSE
                INSERT INTO paper_confirmation_projection_queue(kind,id) VALUES(TG_ARGV[0],row_id);
            END IF;
            IF TG_ARGV[0]='O' THEN
                wallet:=CASE WHEN TG_OP='DELETE' THEN OLD.copied_trader_wallet ELSE NEW.copied_trader_wallet END;
                asset:=CASE WHEN TG_OP='DELETE' THEN OLD.asset_id ELSE NEW.asset_id END;
                INSERT INTO paper_confirmation_projection_queue(kind,id)
                    SELECT 'R',id FROM strategy_market_paper_runs WHERE paper_order_id=row_id
                    UNION ALL SELECT 'F',id FROM paper_fills WHERE paper_order_id=row_id
                    UNION ALL SELECT 'S',id FROM paper_position_settlements WHERE copied_trader_wallet=wallet AND asset_id=asset
                    ;
                IF TG_OP='UPDATE' AND (OLD.copied_trader_wallet,OLD.asset_id) IS DISTINCT FROM (wallet,asset) THEN
                    INSERT INTO paper_confirmation_projection_queue(kind,id) SELECT 'S',id FROM paper_position_settlements
                        WHERE copied_trader_wallet=OLD.copied_trader_wallet AND asset_id=OLD.asset_id
                        ;
                END IF;
            ELSIF TG_ARGV[0]='R' THEN
                IF TG_OP<>'DELETE' THEN
                    INSERT INTO paper_confirmation_projection_queue(kind,id) SELECT 'R',id FROM strategy_market_paper_runs
                        WHERE paper_order_id=NEW.paper_order_id AND id<>row_id ;
                END IF;
                IF TG_OP<>'INSERT' THEN
                    INSERT INTO paper_confirmation_projection_queue(kind,id) SELECT 'R',id FROM strategy_market_paper_runs
                        WHERE paper_order_id=OLD.paper_order_id AND id<>row_id ;
                END IF;
            ELSIF TG_ARGV[0]='F' THEN
                IF TG_OP<>'DELETE' THEN
                    INSERT INTO paper_confirmation_projection_queue(kind,id) SELECT 'S',s.id FROM paper_position_settlements s
                        JOIN paper_orders o ON o.copied_trader_wallet=s.copied_trader_wallet AND o.asset_id=s.asset_id
                        WHERE o.id=NEW.paper_order_id ;
                END IF;
                IF TG_OP<>'INSERT' THEN
                    INSERT INTO paper_confirmation_projection_queue(kind,id) SELECT 'S',s.id FROM paper_position_settlements s
                        JOIN paper_orders o ON o.copied_trader_wallet=s.copied_trader_wallet AND o.asset_id=s.asset_id
                        WHERE o.id=OLD.paper_order_id ;
                END IF;
            END IF;
            RETURN NULL;
        END $$;
        CREATE TRIGGER paper_confirmation_projection_order AFTER INSERT OR UPDATE OR DELETE ON paper_orders
            FOR EACH ROW EXECUTE FUNCTION paper_confirmation_projection_enqueue('O');
        CREATE TRIGGER paper_confirmation_projection_run AFTER INSERT OR UPDATE OR DELETE ON strategy_market_paper_runs
            FOR EACH ROW EXECUTE FUNCTION paper_confirmation_projection_enqueue('R');
        CREATE TRIGGER paper_confirmation_projection_settlement AFTER INSERT OR UPDATE OR DELETE ON paper_position_settlements
            FOR EACH ROW EXECUTE FUNCTION paper_confirmation_projection_enqueue('S');
        CREATE TRIGGER paper_confirmation_projection_fill AFTER INSERT OR UPDATE OR DELETE ON paper_fills
            FOR EACH ROW EXECUTE FUNCTION paper_confirmation_projection_enqueue('F');

        CREATE FUNCTION paper_confirmation_projection_refresh(k text, source_id uuid, captured timestamptz,
            observed_confirmed boolean DEFAULT NULL)
            RETURNS void LANGUAGE plpgsql AS $$
        DECLARE sid uuid; n bigint:=1; c bigint:=0; verified boolean:=false; net_value numeric; basis numeric;
            fees_ok boolean:=false; at_utc timestamptz; correction bigint:=0; deferral bigint:=0; h integer; future boolean;
        BEGIN
            DELETE FROM paper_confirmation_projection_members WHERE kind=k AND id=source_id;
            IF k='O' THEN
                SELECT '00000000-0000-0000-0000-000000000000'::uuid,confirmed,
                    CASE WHEN confirmed AND confirmation_evidence->>'corrected'='true' THEN 1 ELSE 0 END,
                    CASE WHEN NOT confirmed AND confirmation_evidence ? 'last_error' THEN 1 ELSE 0 END
                INTO sid,verified,correction,deferral FROM paper_orders WHERE id=source_id;
                IF sid IS NOT NULL OR observed_confirmed IS NOT NULL THEN
                    INSERT INTO paper_confirmation_projection_seen(id) VALUES(source_id) ON CONFLICT DO NOTHING;
                    IF FOUND THEN UPDATE paper_confirmation_projection_state SET arrivals=arrivals+1; END IF;
                    IF COALESCE(verified,false) OR COALESCE(observed_confirmed,false) THEN
                        UPDATE paper_confirmation_projection_seen SET confirmed=true WHERE id=source_id AND NOT confirmed;
                        IF FOUND THEN UPDATE paper_confirmation_projection_state SET unique_confirmations=unique_confirmations+1; END IF;
                    END IF;
                END IF;
            ELSIF k='R' THEN
                SELECT r.strategy_id,CASE WHEN r.status='Settled' THEN 1 ELSE 0 END,
                    COALESCE(o.confirmed AND o.strategy_id=r.strategy_id AND o.condition_id=r.condition_id
                        AND o.asset_id=r.selected_asset_id AND o.outcome=r.selected_outcome
                        AND (SELECT count(*) FROM strategy_market_paper_runs x WHERE x.paper_order_id=o.id)=1,false),
                    r.net_realized_pnl_usd,r.stake_usd+r.fee_usd,
                    lower(r.fee_accounting_status) IN ('calculated','venuereported') AND r.fee_usd>=0
                        AND r.net_realized_pnl_usd=r.realized_pnl_usd-r.fee_usd,r.settled_at_utc
                INTO sid,c,verified,net_value,basis,fees_ok,at_utc
                FROM strategy_market_paper_runs r LEFT JOIN paper_orders o ON o.id=r.paper_order_id WHERE r.id=source_id;
            ELSIF k='S' THEN
                SELECT CASE WHEN lower(s.copied_trader_wallet) LIKE 'strategy:%' THEN st.id ELSE follow.id END,1,
                    EXISTS(SELECT 1 FROM paper_orders o WHERE o.copied_trader_wallet=s.copied_trader_wallet
                        AND o.asset_id=s.asset_id AND EXISTS(SELECT 1 FROM paper_fills f WHERE f.paper_order_id=o.id))
                    AND NOT EXISTS(SELECT 1 FROM paper_orders o WHERE o.copied_trader_wallet=s.copied_trader_wallet
                        AND o.asset_id=s.asset_id AND (NOT o.confirmed OR o.condition_id<>s.condition_id OR o.outcome<>s.outcome)),
                    s.net_realized_pnl_usd,s.cost_basis_usd+s.fee_usd,
                    lower(s.fee_accounting_status) IN ('calculated','venuereported') AND s.fee_usd>=0
                        AND s.net_realized_pnl_usd=s.realized_pnl_usd-s.fee_usd
                INTO sid,c,verified,net_value,basis,fees_ok FROM paper_position_settlements s
                LEFT JOIN strategies st ON lower(s.copied_trader_wallet)=lower('strategy:'||st.code)
                LEFT JOIN strategies follow ON follow.id='f0110a0d-1ead-4c00-8b01-000000000001'::uuid WHERE s.id=source_id;
            ELSIF k='F' THEN
                SELECT o.strategy_id,1,o.confirmed,f.net_realized_pnl_usd,
                    f.price*f.size_shares-f.net_realized_pnl_usd,
                    lower(f.fee_accounting_status) IN ('calculated','venuereported') AND f.fee_usd>=0
                        AND f.net_realized_pnl_usd IS NOT NULL AND f.realized_pnl_usd-f.net_realized_pnl_usd>=f.fee_usd
                INTO sid,c,verified,net_value,basis,fees_ok FROM paper_fills f JOIN paper_orders o ON o.id=f.paper_order_id
                WHERE f.id=source_id AND lower(o.side)='sell';
            END IF;
            IF sid IS NULL THEN RETURN; END IF;
            FOREACH h IN ARRAY ARRAY[0,1,6,24] LOOP
                IF h>0 AND (k<>'R' OR c=0 OR at_utc IS NULL OR at_utc<captured-make_interval(hours=>h)) THEN CONTINUE; END IF;
                future:=h>0 AND at_utc>captured;
                INSERT INTO paper_confirmation_projection_members VALUES(k,source_id,h,sid,
                    CASE WHEN h=0 THEN NULL WHEN future THEN at_utc ELSE at_utc+make_interval(hours=>h) END,
                    CASE WHEN future THEN 0 ELSE n END,CASE WHEN future THEN 0 ELSE c END,
                    CASE WHEN NOT future AND verified AND (c=1 OR k='O') THEN 1 ELSE 0 END,
                    CASE WHEN NOT future AND verified AND c=1 AND fees_ok THEN net_value ELSE 0 END,
                    CASE WHEN NOT future AND verified AND c=1 AND fees_ok THEN basis ELSE 0 END,
                    CASE WHEN NOT future AND verified AND c=1 AND NOT COALESCE(fees_ok,false) THEN 1 ELSE 0 END,correction,deferral,COALESCE(future,false));
            END LOOP;
        END $$;
        """;
}
