# ERD Físico v1 — PostgreSQL

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-15

## Convenções
PK `uuid`; instantes `timestamptz`; horários recorrentes `time`; dinheiro `numeric(12,2)`; dados tenant-owned carregam `tenant_id`. Privacy by Design: evitar duplicação de PII e exposição em logs.

## Entidades principais
Tenant é fronteira independente de propriedade, segurança e cobrança. Business representa negócio/marca; LegalEntity identidade fiscal; Location unidade operacional. Service e Professional pertencem ao Business. Customer é Business-scoped. Appointment sempre ocorre em Location e contém 1..N AppointmentItems.

## LegalEntity / CPF-CNPJ — APROVADO
```text
legal_entities
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
entity_type varchar(20) NOT NULL
document_type varchar(10) NOT NULL
document_encrypted text NOT NULL
document_fingerprint varchar(64) NOT NULL
country_code varchar(2) NOT NULL DEFAULT 'BR'
legal_name varchar(200) NOT NULL
trade_name varchar(200) NULL
is_primary boolean NOT NULL DEFAULT true
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
UNIQUE(tenant_id,document_type,document_fingerprint)
```
CPF/CNPJ nunca PK; fingerprint HMAC-SHA-256; mesmo documento permitido em Tenants distintos sem relacionamento/exposição cross-tenant.

## Multi-location
```text
Tenant -> Business
            -> Locations
            -> Services -> LocationService
            -> Professionals -> ProfessionalLocation
                             -> ProfessionalService
```
AvailabilityRule = Professional + Location. ScheduleBlock sempre Location-scoped e opcionalmente Professional-scoped.

## LocationService — APROVADO PARA MIGRATION 001
```text
location_services
tenant_id uuid NOT NULL
location_id uuid NOT NULL FK -> locations.id
service_id uuid NOT NULL FK -> services.id
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
PK(tenant_id,location_id,service_id)
```
Sem `price_override`/`duration_minutes_override` na Migration 001. Service é fonte autoritativa de preço/duração no MVP.

## Appointment
```text
appointments
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
location_id uuid NOT NULL FK -> locations.id
customer_id uuid NOT NULL FK -> customers.id
professional_id uuid NOT NULL FK -> professionals.id
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
total_price_snapshot numeric(12,2) NOT NULL
total_duration_minutes_snapshot integer NOT NULL
status varchar(40) NOT NULL
reservation_expires_at timestamptz NULL
cancellation_reason varchar(500) NULL
cancelled_at timestamptz NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```
AppointmentItems preservam snapshots de serviço/preço/duração para verdade histórica e Analytics.

## Payment / PaymentAttempt / Refund
Payment é obrigação financeira lógica; PaymentAttempt é cada tentativa concreta no provider; Refund integral no MVP referencia Payment e PaymentAttempt confirmado. `Payment.Amount == Appointment.TotalPriceSnapshot` para SERVICE.

## Retenção e Analytics — APROVADO
A retenção separa conteúdo efêmero de fatos comerciais. Conversation/WhatsApp, conteúdo LLM, webhook bruto e logs detalhados possuem retenção curta por categoria. Appointment, AppointmentItem/snapshots, Payment, PaymentAttempt e Refund preservam fatos necessários ao histórico operacional, financeiro e analítico conforme finalidade e obrigações aplicáveis.

O Dashboard deve suportar por Tenant/período e Location quando aplicável:
- receita realizada/prevista e ticket médio;
- appointments, cancelamentos, refunds e no-show;
- serviços mais utilizados, receita e tendências por serviço;
- desempenho por Professional: atendimentos, receita, ticket médio, cancelamentos/no-show;
- clientes novos, recorrentes, ativos e inativos;
- última visita concluída;
- comparações entre períodos equivalentes;
- tendências por Location.

Cliente inativo é calculado pela última visita concluída + threshold configurável pelo Tenant (baseline de produto: 60 dias). Não persistir `IsInactive` no Customer no MVP.

Analytics inicialmente consulta PostgreSQL transacional. Projeções/agregações entram somente quando volume/hot paths justificarem. Não criar Data Warehouse no MVP.

Regra: anonimização/expurgo de PII, quando aplicável, não deve destruir fatos comerciais/agregados legítimos necessários ao histórico do Tenant.

## Anti-double-booking
```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
ALTER TABLE appointments
ADD CONSTRAINT ex_appointments_no_overlap
EXCLUDE USING gist (
  tenant_id WITH =,
  professional_id WITH =,
  tstzrange(starts_at, ends_at, '[)') WITH &&
)
WHERE (status IN ('PENDING','CONFIRMED','CONFIRMED_BY_CLIENT','RESCHEDULE_REQUESTED'));
```
LocationId não entra: Professional não pode estar simultaneamente em duas Locations.

## Hot path Scheduling
```text
Tenant + Location
 -> LocationService
 -> ProfessionalLocation
 -> ProfessionalService
 -> AvailabilityRule
 - ScheduleBlock
 - Appointment ativo
 = Slot elegível
```

## Estratégia Multi-Tenant — APROVADA
TenantId é principal barreira estrutural no banco. Consultas partem do TenantContext. Coerência específica de Business fica no domínio/aplicação. Não adicionar BusinessId indiscriminadamente às FKs. Constraints/índices adicionais são orientados por integridade, concorrência e hot paths reais, equilibrando performance e simplicidade.

## Índices prioritários iniciais
```text
legal_entities(tenant_id,document_type,document_fingerprint) UNIQUE
locations(tenant_id,business_id,is_active)
location_services(tenant_id,location_id,is_active)
location_services(tenant_id,service_id,is_active)
professional_locations(tenant_id,location_id,is_active)
professional_locations(tenant_id,professional_id,is_active)
professional_services(tenant_id,professional_id,is_active)
availability_rules(tenant_id,location_id,professional_id,day_of_week,is_active)
schedule_blocks(tenant_id,location_id,professional_id,starts_at,ends_at)
appointments(tenant_id,location_id,starts_at)
appointments(tenant_id,professional_id,starts_at)
appointments(tenant_id,customer_id,starts_at)
appointments(tenant_id,status,starts_at)
payments(tenant_id,appointment_id,payment_purpose)
payment_attempts(tenant_id,payment_id,created_at)
payment_attempts(provider,provider_payment_id) UNIQUE WHERE provider_payment_id IS NOT NULL
refunds(tenant_id,payment_id)
```
Baseline; validar planos de execução/métricas antes de adicionar índices redundantes.

## Pontos antes da primeira migration
- seed oficial de BusinessTypes;
- validar combo sem nesting;
- testes automatizados de isolamento, concorrência, hot paths e idempotência financeira.
