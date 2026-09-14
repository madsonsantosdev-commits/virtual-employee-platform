# Modelo de Domínio v1

> Status: Draft v1 — decisões funcionais aprovadas
> Data: 2026-09-14

## Objetivo
Definir o modelo de domínio inicial da Virtual Employee Platform antes da implementação física da solução .NET e PostgreSQL.

## Princípios
1. Multi-Tenant desde o primeiro dia.
2. Tenant é fronteira de propriedade, segurança e cobrança; não é sinônimo de unidade física.
3. Business representa o negócio/marca; Location representa a unidade operacional.
4. Service e Professional pertencem ao Business; a operação em uma unidade é definida por relacionamentos com Location.
5. Appointment sempre acontece em uma Location.
6. Scheduling é o domínio operacional central.
7. IA interpreta; backend valida; domínio executa.
8. Pagamento só é confirmado por webhook/API confiável do gateway.
9. Preço e duração históricos são preservados por snapshots.
10. Appointments não são apagados fisicamente.
11. Cancelamento operacional e refund financeiro são processos separados.
12. Sem sinal, pagamento parcial, pagamento complementar ou refund parcial no MVP.
13. Double-booking é impedido por validação transacional + constraint PostgreSQL.
14. Um Appointment pode conter um ou mais serviços, mas possui um único Professional no MVP.
15. Privacy by Design: coletar, persistir, expor e compartilhar somente os dados necessários à finalidade.

---

## Estrutura organizacional

### Tenant
Fronteira lógica de propriedade, isolamento de dados, segurança e cobrança do assinante. Um Tenant não representa obrigatoriamente um estabelecimento físico.

### Business
Representa o negócio/marca operado pelo Tenant. Mantém políticas comerciais como cancelamento/refund e granularidade de início de agenda (`SlotIntervalMinutes`, default 15).

No MVP a experiência inicial será simples: 1 Tenant -> 1 Business -> 1 Location. O modelo suporta 1 Business -> N Locations.

### BusinessType
Classificação do negócio. Tipos oficiais possuem escopo global; tipos customizados pertencem ao Tenant. Unicidade customizada é tenant-scoped.

### LegalEntity
Representa identidade jurídica/fiscal associada ao Business.

Campos conceituais: `TenantId`, `BusinessId`, `EntityType (PERSON|COMPANY)`, `DocumentType (CPF|CNPJ)`, `DocumentNumber`, `CountryCode`, `LegalName`, `TradeName`, `IsPrimary`, `IsActive`.

Regras de privacidade e domínio:
- CPF/CNPJ normalizados e validados no backend;
- documento fiscal nunca é PK;
- documento completo não aparece em logs;
- UI mascara documento quando visualização integral não for necessária;
- alteração de dados fiscais é auditável;
- acesso requer autorização;
- IA não recebe CPF/CNPJ sem necessidade operacional explícita;
- retenção/exclusão segue política definida por finalidade e obrigação aplicável.

### Location
Unidade operacional física ou virtual. Mantém nome, telefone, endereço, país e timezone. Pode referenciar LegalEntity.

> **Tenant = fronteira de propriedade, segurança e cobrança. Business = negócio/marca. Location = unidade operacional física ou virtual.**

Franquias não serão módulo do MVP. Franqueados independentes podem futuramente possuir Tenants separados.

---

## Catálogo, profissionais e unidades

### Service
Service pertence ao Business e representa o catálogo comercial da marca.

Campos principais: `ServiceId`, `TenantId`, `BusinessId`, nome, descrição, tipo `SINGLE|COMBO`, preço padrão, duração padrão, `RequiresPayment` e `IsActive`.

Preço/duração são definidos pelo backend; combo possui valores próprios; alterações não modificam histórico; sem combo aninhado no MVP.

### LocationService
Define se um Service do Business está disponível em uma determinada Location.

Responsabilidades:
- habilitar/desabilitar serviço por unidade;
- permitir evolução futura para `PriceOverride` e `DurationOverride` por unidade sem duplicar o Service.

No MVP, preço e duração usados por padrão são os definidos em `Service`. Overrides podem existir no modelo como evolução, mas não precisam ser expostos na primeira UX/API.

### ServiceComponent
Composição comercial de COMBO. Componentes SINGLE; não calculam automaticamente preço/duração; sem nesting.

### Professional
Professional pertence ao Business, não a uma Location específica. Isso evita duplicar o mesmo profissional quando ele trabalha em mais de uma unidade.

Dados pessoais de Professional devem ser limitados aos necessários para operação e administração.

### ProfessionalLocation
Define em quais Locations um Professional pode operar.

Exemplo:
```text
Arthur -> Moema
Arthur -> Tatuapé
```

O profissional continua sendo uma única entidade do Business.

### ProfessionalService
Matriz Professional × Service. Profissional elegível deve estar habilitado para todos os serviços selecionados. Sem entidade Skill separada no MVP.

A elegibilidade operacional para uma reserva exige simultaneamente:
1. Service disponível na Location via LocationService;
2. Professional associado à Location via ProfessionalLocation;
3. Professional habilitado para todos os Services via ProfessionalService.

### AvailabilityRule
Agenda-base recorrente do profissional **em uma Location**. Portanto inclui `LocationId`.

Isso permite, por exemplo:
```text
Arthur
- segunda: Moema
- terça: Tatuapé
```

### ScheduleBlock
Exceção/indisponibilidade vinculada a uma Location e opcionalmente a um Professional.

- `ProfessionalId != null`: bloqueia aquele profissional naquela Location;
- `ProfessionalId = null`: bloqueia a Location inteira.

Sistema apresenta impacto e oferece alternativas; nenhum cliente é movido sem consentimento.

### Customer
Cliente final. Telefone/WhatsApp é identificador operacional principal, mas não PK.

Dados previstos no MVP: nome opcional, telefone/WhatsApp obrigatório operacionalmente e e-mail opcional. Evitar enriquecer Customer com dados pessoais sem finalidade definida.

---

## Appointment — Aggregate Root
Principal agregado operacional do Scheduling.

Campos principais: `AppointmentId`, `TenantId`, `BusinessId`, `LocationId`, `CustomerId`, `ProfessionalId`, `StartsAt`, `EndsAt`, `TotalPriceSnapshot`, `TotalDurationMinutesSnapshot`, `Status`, `ReservationExpiresAt`, cancelamento e auditoria.

### Regra de unidade
Todo Appointment ocorre em exatamente uma Location.

Antes da criação/reagendamento, o backend valida:
- Location ativa e pertencente ao Business;
- todos os Services disponíveis nessa Location;
- Professional vinculado à Location;
- Professional habilitado para todos os Services;
- AvailabilityRules daquela Location;
- ScheduleBlocks daquela Location;
- conflitos de Appointment.

### AppointmentItem
Appointment possui 1..N itens, preservando ServiceId, nome, preço e duração em snapshot. Sem quantity no MVP.

Snapshots devem preservar apenas dados comerciais necessários ao histórico; não duplicar PII do Customer/Professional.

### Totais
```text
SUM(AppointmentItems.PriceSnapshot) -> Appointment.TotalPriceSnapshot -> Payment.Amount
SUM(AppointmentItems.DurationMinutesSnapshot) -> Appointment.TotalDurationMinutesSnapshot -> EndsAt
```

Combo usa preço/duração próprios.

### Fluxo
```text
Selecionar Location
-> selecionar 1..N Services disponíveis na Location
-> backend calcula preço/duração
-> opcionalmente sugere combo
-> filtra Professionals da Location
-> mantém apenas os habilitados para TODOS os Services
-> consulta agenda daquele Professional naquela Location
-> busca janela contínua
-> cliente escolhe
-> CreateAppointment revalida
-> PENDING
-> pagamento integral
-> webhook confiável
-> CONFIRMED
```

### Status
`PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `EXPIRED`, `COMPLETED`, `NO_SHOW`.

### Invariantes
1. StartsAt < EndsAt.
2. Pelo menos um AppointmentItem.
3. Duração total corresponde ao intervalo.
4. Preço total corresponde à soma dos itens.
5. Location pertence ao Business e está ativa.
6. Todos os Services estão disponíveis na Location.
7. Professional pertence operacionalmente à Location.
8. Professional está habilitado para todos os Services.
9. Intervalo respeita AvailabilityRules, ScheduleBlocks e appointments ativos da Location.
10. Sem sobreposição ativa para mesmo profissional.
11. CreateAppointment recalcula preço/duração/disponibilidade.
12. Pago só vira CONFIRMED após confirmação financeira confiável.
13. Mudança financeira após pagamento requer cancelamento/novo booking no MVP.

### AppointmentHistory
Preserva auditoria operacional. Registrar mudança necessária sem copiar PII desnecessária; preferir IDs, ator, timestamps, estados, Location e motivo operacional.

---

## Payment e Refund
Payment integral via PIX, CREDIT_CARD ou DEBIT_CARD. `Payment.Amount == Appointment.TotalPriceSnapshot`. Checkout hospedado/tokenizado; dados brutos de cartão não trafegam pela aplicação; confirmação apenas por webhook/API confiável.

Refund integral e assíncrono. Nunca comunicar REFUNDED antes da confirmação do gateway.

## Conversation
Estado conversacional não substitui Appointment. IA interpreta e solicita ferramentas estruturadas, sem acesso direto ao banco.

Fluxo de IA:
```text
Canal -> Conversation Engine -> Context Builder/Data Minimization -> AI Gateway -> LLM
```

## Subscription e unidade faturável
Billing SaaS separado de Customer Payments. Mensal: cartão recorrente ou Pix. Anual: cartão recorrente. Sem boleto.

Subscription não significa automaticamente todas as unidades. `SubscriptionUnit` associa assinatura às Locations faturáveis.

## UsageRecord
Mede consumo/custos. Telemetria deve usar IDs técnicos e evitar conteúdo pessoal/conversacional quando não necessário. `LocationId` pode ser incluído quando o consumo puder ser atribuído a uma unidade específica.

---

## Privacidade / LGPD — invariantes de domínio
1. Toda nova categoria de dado pessoal exige finalidade documentada antes de entrar no modelo.
2. APIs não retornam PII por conveniência; DTOs expõem somente o necessário ao caso de uso.
3. CPF/CNPJ e identificadores pessoais não são usados como PK.
4. PII não deve ser duplicada em snapshots sem necessidade histórica real.
5. Logs/auditoria preferem IDs e metadados técnicos.
6. Retenção é definida por categoria; soft-delete não é justificativa para retenção eterna.
7. Processos devem permitir localizar/corrigir/exportar/anonimizar/eliminar dados quando juridicamente aplicável.
8. Compartilhamento com provedores externos segue minimização.
9. Papéis Controlador/Operador são avaliados por finalidade/fluxo.
10. Decisão jurídica sobre base legal, retenção obrigatória ou incidente não é delegada ao LLM.

Detalhamento: `docs/architecture/privacy-lgpd-v1.md`.

---

## Disponibilidade e agenda
```text
Location
  -> ProfessionalLocation
  -> ProfessionalService
  -> AvailabilityRule
  - ScheduleBlock
  - Appointments ativos
  = janelas livres
```

Para múltiplos serviços: Services disponíveis na Location -> interseção de ProfessionalServices -> duração total -> janela contínua. Slots não são persistidos.

Imprevistos: indisponibilidade na Location -> impacto -> confirmação -> ScheduleBlock -> alternativas -> mensagem -> cliente escolhe -> revalidação -> reagendamento.

## Concorrência
Consultar disponibilidade não garante vaga. CreateAppointment revalida e constraint PostgreSQL é última barreira. Perdedor recebe SLOT_UNAVAILABLE e alternativas.

## Relacionamentos conceituais
```text
Tenant -> Business
Business -> BusinessType
Business -> LegalEntities
Business -> Locations
Location -> LegalEntity (opcional)
Subscription <-> Location via SubscriptionUnit
Business -> Services
Location <-> Service via LocationService
Service(COMBO) -> ServiceComponents -> Services(SINGLE)
Business -> Professionals
Professional <-> Location via ProfessionalLocation
Professional <-> Service via ProfessionalService
Professional + Location -> AvailabilityRules
Location -> ScheduleBlocks
Business -> Customers
Customer -> Appointments
Location -> Appointments
Professional -> Appointments
Appointment -> AppointmentItems -> Service
Appointment -> AppointmentHistory
Appointment -> Payment -> Refund
Tenant -> Conversations / Subscription / UsageRecords
```

## Regra central
> **IA interpreta -> Backend valida -> Domínio executa.**

Para operações financeiras:
> **IA interpreta -> Usuário autoriza quando necessário -> Backend valida -> Gateway processa -> Webhook confirma.**

Para privacidade:
> **Coletar o necessário -> limitar finalidade/acesso -> compartilhar o mínimo -> reter pelo período definido -> atender direitos do titular.**
