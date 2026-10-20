# ERD Físico v1 — PostgreSQL

> Status: 🔒 FROZEN v1 — decisões estruturais aprovadas para a primeira migration
> Data: 2026-09-15

## Convenções
PK `uuid`; instantes `timestamptz`; horários recorrentes `time`; dinheiro `numeric(12,2)`; dados tenant-owned carregam `tenant_id`. Privacy by Design: evitar duplicação de PII e exposição em logs.

### Normalização e validação de dados — APROVADO
Dados de entrada são normalizados e validados antes da persistência. Representações canônicas adicionais são armazenadas somente quando necessárias para identidade, unicidade, busca ou segurança. Valores de apresentação são preservados quando possuem significado para o usuário. O PostgreSQL continua protegendo invariantes críticas por constraints.

Exemplos: CPF/CNPJ são normalizados antes de validação/fingerprint; telefone utiliza representação canônica; codes seguem padrão canônico; nomes comerciais preservam sua representação de apresentação. Não criar colunas `NormalizedX` indiscriminadamente.

## Entidades principais
Tenant é fronteira independente de propriedade, segurança e cobrança. Business representa negócio/marca; LegalEntity identidade fiscal; Location unidade operacional. Service e Professional pertencem ao Business. Customer é Business-scoped. Appointment sempre ocorre em Location e contém 1..N AppointmentItems.

## BusinessType — CATÁLOGO GLOBAL APROVADO
`BusinessType` é catálogo global reutilizável da plataforma e não pertence a um Tenant. Todo tipo possui `code` obrigatório e único. Novos tipos descobertos durante onboarding podem tornar-se opções para futuros Tenants, com normalização, prevenção de duplicidade e mecanismo de curadoria.

```text
business_types
id uuid PK
code varchar(80) NOT NULL
name varchar(120) NOT NULL
is_system boolean NOT NULL DEFAULT false
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
UNIQUE(code)
```

Seeds oficiais iniciais: `BARBERSHOP`, `BEAUTY_SALON`, `NAIL_STUDIO`, `AESTHETICS`, `MASSAGE`, `PERSONAL_TRAINER`, `HAIR_STYLIST`, `EYEBROW_LASH`, `TATTOO_PIERCING`, `OTHER`.

BusinessType orienta onboarding, UX, contexto da IA e Analytics da plataforma; não introduz condicionais rígidas de domínio por categoria.

## Service e Combo — APROVADO
`Service` representa tanto serviço simples quanto oferta comercial do tipo combo.

```text
services
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NOT NULL
service_type varchar(20) NOT NULL -- SINGLE | COMBO
price numeric(12,2) NOT NULL
duration_minutes integer NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

```text
service_components
tenant_id uuid NOT NULL
combo_service_id uuid NOT NULL FK -> services.id
component_service_id uuid NOT NULL FK -> services.id
sort_order integer NOT NULL
created_at timestamptz NOT NULL
PK(tenant_id,combo_service_id,component_service_id)
```

COMBO contém somente Services SINGLE do mesmo Business, nunca outro COMBO. Não há quantity no MVP. Preço/duração são próprios. Elegibilidade profissional e disponibilidade por Location são explícitas. No Appointment, COMBO gera um único AppointmentItem. Nesting é bloqueado no domínio/aplicação com `COMBO_NESTING_NOT_ALLOWED`.

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

## AvailabilityRule — implementação em 09/10/2026

Detalhamento físico implementado pela migration
`20261009143618_AddAvailabilityRules`.

```text
availability_rules
id uuid PK
tenant_id uuid NOT NULL
location_id uuid NOT NULL
professional_id uuid NOT NULL
day_of_week integer NOT NULL
start_time time without time zone NOT NULL
end_time time without time zone NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL

FK(tenant_id,professional_id,location_id)
  -> professional_locations(tenant_id,professional_id,location_id)
  ON DELETE RESTRICT

CHECK(day_of_week BETWEEN 0 AND 6)
CHECK(start_time < end_time)
```

Índices implementados:
- `(tenant_id,location_id,professional_id,day_of_week,is_active)`
- `(tenant_id,professional_id,location_id)`

A FK garante a existência do vínculo no mesmo tenant.
Estado ativo, coerência de Business e ausência de sobreposição
no conjunto enviado são validados pela Application.
O banco não possui constraint de exclusão para sobreposição dessas regras.

Horários representam recorrência local; CreatedAt e UpdatedAt são
instantes absolutos. TenantId é configurado como token de concorrência
no EF Core, além do filtro de consulta e da validação de escrita.

## ScheduleBlock e idempotência — implementação em 14/10/2026

Migrations:
- `20261014130625_AddScheduleBlocks`
- `20261014183203_AddIdempotencyRecords`

```text
schedule_blocks
id uuid PK
tenant_id uuid NOT NULL
location_id uuid NOT NULL
professional_id uuid NULL
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL

FK(tenant_id,location_id) -> locations(tenant_id,id)
FK(tenant_id,professional_id,location_id)
  -> professional_locations(tenant_id,professional_id,location_id)
ON DELETE RESTRICT
CHECK(starts_at < ends_at)

idempotency_records
tenant_id uuid NOT NULL FK -> tenants(id) ON DELETE RESTRICT
operation varchar(100) NOT NULL
idempotency_key varchar(200) NOT NULL
request_hash varchar(64) NOT NULL
response_json jsonb NOT NULL
created_at timestamptz NOT NULL
PK(tenant_id,operation,idempotency_key)
```

ProfessionalId null permite bloqueio da unidade inteira.
A FK obrigatória de Location mantém a referência dentro do tenant.

Índices de ScheduleBlock:
- `(tenant_id,location_id,professional_id,starts_at,ends_at)`
- `(tenant_id,professional_id,location_id)`

Ambas as entidades usam filtro de tenant e proteção de escrita no contexto.
TenantId é token de concorrência no EF Core.
A chave composta de idempotência impede duplicação da operação;
a transação garante a gravação conjunta do bloqueio e da resposta.

## Customer — implementação em 15/10/2026

Customer pertence a um Business e ao tenant atual.

```text
customers
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL
name text NULL
phone text NOT NULL
email text NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Restrições e índices implementados:
- FK composta `(tenant_id, business_id)` para
  `businesses(tenant_id, id)`, com exclusão restrita.
- CHECK `CK_customers_phone_not_blank` exige
  `length(btrim(phone)) > 0`.
- Índice não único em `(tenant_id, business_id, phone)`.
- Filtro de consulta por tenant e proteção de escrita no AppDbContext.

A Application normaliza o telefone antes da persistência.
O CHECK do banco impede telefone vazio ou composto apenas por espaços
comuns; não valida o formato internacional.

Telefone não é PK e não possui restrição de unicidade.
Datas de auditoria são gravadas em UTC.
A tabela não possui campos de estado ativo ou inativo nesta entrega.

Migration: `20261015133227_AddCustomers`.

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

### Implementação incremental de Appointments — 16/10/2026

A tabela appointments está implementada com os campos descritos acima.
Desde 19/10/2026, inclui cancellation_reason varchar(500) NULL
e cancelled_at timestamptz NULL.

A restrição CK_appointments_cancellation exige:
- Estados cancelados com motivo não nulo e não vazio após btrim.
- Data de cancelamento não nula, entre created_at e updated_at.
- Demais estados com motivo e data de cancelamento nulos.

A coluna varchar(500) limita o tamanho do motivo.
A Application e o domínio aplicam Trim e validam o limite antes da escrita.

A idempotência de cancelamento reutiliza idempotency_records,
com Operation = CancelAppointment.
Estado, auditoria e resposta são persistidos na mesma transação.
A exclusão de sobreposição deixa de considerar o registro cancelado.

Migrations de cancelamento:
- 20261019204922_AddAppointmentCancellation
- 20261019231745_LimitAppointmentCancellationReason

```text
appointment_items
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL
service_id uuid NOT NULL
service_name_snapshot text NOT NULL
price_snapshot numeric(12,2) NOT NULL
duration_minutes_snapshot integer NOT NULL
```

Referências implementadas, com exclusão restrita:
- appointments(tenant_id, business_id) -> businesses(tenant_id, id);
- appointments(tenant_id, customer_id) -> customers(tenant_id, id);
- appointments(tenant_id, professional_id, location_id) ->
  professional_locations(tenant_id, professional_id, location_id);
- appointment_items(tenant_id, appointment_id) ->
  appointments(tenant_id, id);
- appointment_items(tenant_id, service_id) -> services(tenant_id, id).

Chaves e restrições:
- Chave alternativa (tenant_id, id) em appointments e customers.
- Índice único em appointment_items(tenant_id, appointment_id, service_id).
- CHECKs exigem intervalo válido, duração positiva e preço não negativo.
- EndsAt deve corresponder a StartsAt mais a duração total em minutos.
- Status é limitado aos estados operacionais previstos.
- PENDING exige expiração posterior à criação e não posterior ao início.
- Itens exigem nome de snapshot não vazio, preço não negativo e duração positiva.
- Appointments e itens possuem filtros de consulta e proteção de escrita
  por tenant; TenantId é token de concorrência.
- Status de Appointment também é token de concorrência.

A constraint ex_appointments_no_overlap usa btree_gist e protege
TenantId + ProfessionalId + intervalo [StartsAt, EndsAt), sem LocationId.
Aplica-se a PENDING, CONFIRMED, CONFIRMED_BY_CLIENT e RESCHEDULE_REQUESTED.
A passagem do prazo não remove a restrição: EXPIRED precisa ser persistido.

Índices de appointments:
- (tenant_id, business_id);
- (tenant_id, customer_id, starts_at);
- (tenant_id, location_id, starts_at);
- (tenant_id, professional_id, location_id);
- (tenant_id, professional_id, starts_at);
- (tenant_id, status, reservation_expires_at).

AppointmentItems também possui índice em (tenant_id, service_id).

A idempotência reutiliza idempotency_records, com Operation = CreateAppointment.
Appointment, itens e resposta original são gravados na mesma transação.
A mesma chave lógica é serializada por advisory lock transacional;
a proteção de horário permanece na exclusion constraint.

Migrations:
- 20261016170029_AddAppointments;
- 20261016190726_AddAppointmentOverlapProtection.

Histórico de transições e operações de reagendamento permanecem
para próximas etapas. Cancelamento está implementado desde 19/10/2026.

## Payment / PaymentAttempt / Refund
Payment é obrigação financeira lógica; PaymentAttempt é cada tentativa concreta no provider; Refund integral no MVP referencia Payment e PaymentAttempt confirmado. `Payment.Amount == Appointment.TotalPriceSnapshot` para SERVICE.

## Retenção e Analytics — APROVADO
Conteúdo conversacional/técnico efêmero tem retenção mínima necessária; fatos comerciais e financeiros preservam histórico legítimo do Tenant. Analytics inicialmente consulta PostgreSQL transacional e projeções/agregações entram somente quando volume/hot paths justificarem. Cliente inativo é derivado da última visita concluída + threshold configurável (baseline 60 dias), sem `IsInactive` persistido.

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

## Baseline de testes arquiteturais — APROVADO
Invariantes críticas de isolamento, concorrência e integridade financeira exigem testes automatizados de integração contra PostgreSQL real. Regras puras de domínio usam unit tests. Hot paths possuem testes de performance mensuráveis; otimizações são orientadas por evidência.

Baseline obrigatória:
- Multi-Tenant: provar que Tenant A não lê, altera, referencia ou descobre dados tenant-owned do Tenant B; mesmo CPF/CNPJ em Tenants distintos permanece isolado;
- Concorrência: requisições simultâneas para o mesmo Professional/período resultam em apenas um Appointment ocupante; a concorrente perde com `SLOT_UNAVAILABLE`/alternativas;
- Scheduling: medir `availability/slots/search` com dataset representativo, registrar baseline e usar `EXPLAIN ANALYZE`/métricas antes de adicionar otimizações ou índices;
- Financeiro: webhook duplicado e retry não duplicam processamento; `PaymentAttempt` respeita idempotency key; refund é idempotente; confirmação financeira tardia após Appointment EXPIRED não reativa agenda e dispara fluxo compensatório apropriado.

## Índices prioritários iniciais
```text
business_types(code) UNIQUE
legal_entities(tenant_id,document_type,document_fingerprint) UNIQUE
service_components(tenant_id,combo_service_id)
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

## Freeze v1
ERD v1 congelado em 2026-09-15 para implementação da primeira migration. Novas ideias não críticas entram no backlog. Alterações estruturais posteriores exigem decisão explícita e migration versionada; não reabrir o ERD v1 informalmente.
