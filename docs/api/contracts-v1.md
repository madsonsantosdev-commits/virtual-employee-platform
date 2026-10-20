# Contratos da API v1

> Status: 🔒 FROZEN v1 — contratos aprovados para implementação
> Data: 2026-09-17

Base: `/api/v1`. JSON, UUID, ISO-8601. `TenantId` vem exclusivamente do contexto autenticado/confiável e não é aceito do cliente como autoridade. Requisições propagam `X-Correlation-Id`.

## 0. Convenções globais

### Normalização e validação
A API aceita entradas amigáveis, normaliza apenas o que for tecnicamente seguro, valida estrutura e regras antes de persistir e não corrige silenciosamente dados semanticamente inválidos.

Fluxo: `Request -> Normalização segura -> Validação estrutural -> Validação de domínio -> Application/Domain -> Persistência`.

- nomes/descrições: trim e normalização de espaços, preservando apresentação;
- e-mail: trim, validação e representação apropriada para comparação;
- telefone: representação canônica, preferencialmente E.164;
- CPF/CNPJ: remover máscara, validar e aplicar a estratégia de proteção/fingerprint definida na arquitetura;
- códigos canônicos de catálogo: gerados/normalizados pelo backend;
- preço: decimal positivo e precisão monetária apropriada;
- duração: inteiro positivo em minutos;
- UUID: formato válido, existência e pertencimento ao contexto permitido;
- instantes: ISO-8601 e persistência `timestamptz`;
- horários recorrentes: hora local interpretada no timezone da `Location`;
- enums: somente valores conhecidos pela versão da API.

Normalização técnica pertence à fronteira de aplicação; invariantes de negócio permanecem no Domain/Application. PostgreSQL continua protegendo invariantes críticas.

### Idempotência
Operações críticas usam `Idempotency-Key` obrigatório: criar Appointment, reagendar, cancelar, criar nova PaymentAttempt, solicitar Refund e criar ScheduleBlock. GETs não exigem chave.

A identidade idempotente considera conceitualmente `TenantId + Operation + IdempotencyKey`, associada a hash do request e resultado. Mesma chave + mesmo payload retorna o mesmo resultado sem repetir o efeito. Mesma chave + payload diferente retorna `409 IDEMPOTENCY_CONFLICT`.

Idempotência da API não substitui proteção de concorrência do banco. Webhooks usam identidade própria do provider (`Provider + ProviderEventId`). Operações em gateways também propagam chave idempotente própria quando suportado.

### Problem Details e HTTP Status
Erros seguem RFC 9457 Problem Details, estendido com `code`, `correlationId` e, quando aplicável, `errors`.

```json
{
  "type": "https://api.virtualemployee.com/problems/slot-unavailable",
  "title": "Horário indisponível",
  "status": 409,
  "code": "SLOT_UNAVAILABLE",
  "detail": "O horário selecionado não está mais disponível.",
  "correlationId": "01K5..."
}
```

Convenção: `400` request estruturalmente inválido; `401` não autenticado; `403` sem autorização; `404` recurso inexistente dentro do contexto permitido; `409` concorrência/conflito de estado/idempotência; `422` validação/regra de domínio; `429` rate limit; `500` falha interna; `502/503` dependência externa quando apropriado.

Tentativas de acessar recurso de outro Tenant não revelam existência e são tratadas como `404 RESOURCE_NOT_FOUND`. Respostas de erro nunca expõem stack trace, SQL, segredos ou detalhes sensíveis de providers; diagnóstico interno é correlacionado via `correlationId`/App Insights. `code` é o contrato estável para clientes; mensagens podem evoluir/localizar.

## 1. Business Types e Business
`GET /business-types?search=bar&limit=20`

`POST /business-types`
```json
{"name":"Podologia"}
```

`BusinessType` é catálogo global reutilizável. O cliente não define o `code`; o backend normaliza/gera o código canônico, verifica duplicidade e permite ao Tenant continuar o onboarding. Novos tipos dinâmicos ficam sujeitos a curadoria antes de serem expostos como opção geral para outros Tenants, evitando poluição do catálogo.

Exemplo de resposta:
```json
{"id":"<uuid>","code":"PODOLOGY","name":"Podologia","isActive":true}
```

`GET /business`

Implementado. Retorna o Business pertencente ao tenant atual.

O `TenantId` não é aceito como parâmetro ou autoridade do cliente. A consulta utiliza o `TenantContext` e o Global Query Filter do EF Core. Business inexistente para o tenant atual retorna `404 Not Found`.

Durante o desenvolvimento local, `X-Tenant-Id` é utilizado temporariamente para inicializar o `TenantContext`. Esse mecanismo existe somente em `Development`, não representa autenticação e não deve ser utilizado como autoridade de Tenant em produção.

Response:

```json
{
  "id": "<uuid>",
  "businessTypeId": "<uuid>",
  "name": "Barbearia Exemplo",
  "slotIntervalMinutes": 15,
  "isActive": true
}

## 2. Locations
`GET /locations?active=true`
A implementação inicial de `GET /locations` retorna as Locations visíveis no contexto tenant-scoped atual. `TenantId` não é exposto no response.

A leitura utiliza o `TenantContext` e o Global Query Filter do EF Core. O `TenantId` autoritativo deve vir de contexto autenticado/confiável.

Durante o desenvolvimento local, `X-Tenant-Id` é utilizado temporariamente para inicializar o `TenantContext`. Esse mecanismo existe somente em `Development`, não representa autenticação e não deve ser utilizado como autoridade de Tenant em produção.

`GET /locations/{locationId}`
Implementado. Retorna a Location quando ela pertence ao tenant atual. Location inexistente ou pertencente a outro tenant retorna `404 Not Found`, sem revelar a existência de recursos cross-tenant. A consulta utiliza o `TenantContext` e o Global Query Filter do EF Core.

`POST /locations`
Implementado. Cria uma nova Location vinculada a um Business pertencente ao tenant atual.

O `TenantId` não é aceito no payload. Ele é obtido do `TenantContext`, cuja autoridade em produção deverá vir do contexto autenticado/confiável. Durante o desenvolvimento local, o header temporário `X-Tenant-Id` inicializa esse contexto.

Request:

```json
{
  "businessId": "<uuid>",
  "name": "Unidade Moema",
  "phone": "+5511999999999",
  "address": "Av. Exemplo, 100",
  "countryCode": "BR",
  "timezone": "America/Sao_Paulo"
}
```

`phone` e `address` são opcionais. `businessId`, `name`, `countryCode` e `timezone` são obrigatórios.

Responses:
- `201 Created`: Location criada. A resposta contém a Location e o header `Location` aponta para `/api/v1/locations/{locationId}`.
- `400 Bad Request`: request estruturalmente inválido.
- `404 Not Found`: Business inexistente ou pertencente a outro tenant. Os dois casos são indistinguíveis externamente para não revelar recursos cross-tenant.

A verificação do Business utiliza o `TenantContext` e o Global Query Filter do EF Core. O backend gera o `LocationId`, define o `TenantId` e controla `IsActive`, `CreatedAt` e `UpdatedAt`.

`PUT /locations/{locationId}`

Implementado. Atualiza os dados operacionais de uma Location pertencente ao tenant atual.

O `TenantId`, `BusinessId`, `LocationId` e `CreatedAt` não são aceitos no payload e não podem ser alterados por esta operação. O `TenantId` autoritativo continua vindo do `TenantContext`. Durante o desenvolvimento local, o header temporário `X-Tenant-Id` inicializa esse contexto.

Request:

```json
{
  "name": "Unidade Vila Mariana",
  "phone": "+5511987654321",
  "address": "Rua Domingos de Morais, 1600",
  "countryCode": "BR",
  "timezone": "America/Sao_Paulo",
  "isActive": true
}

```

`name`, `countryCode`, `timezone` e `isActive` compõem os dados atualizáveis obrigatórios da operação. `phone` e `address` são opcionais. O Domain normaliza os dados operacionais antes da persistência.

Responses:
- `200 OK`: Location atualizada e retornada.
- `400 Bad Request`: request estruturalmente inválido.
- `404 Not Found`: Location inexistente ou pertencente a outro tenant. Os dois casos são indistinguíveis externamente para não revelar recursos cross-tenant.

A atualização busca a Location através do `TenantContext` e do Global Query Filter do EF Core. `BusinessId` permanece imutável nesta operação. `UpdatedAt` é controlado pelo backend e atualizado quando a alteração é persistida.

MVP cria uma Location no onboarding, mas contratos não assumem que ela será sempre única.

## 3. Services, Combos e disponibilidade por Location

`GET /services?active=true&locationId=<uuid>`

`POST /services`, `PUT /services/{serviceId}`.

Service pertence ao Business e é a fonte comercial de preço/duração no MVP. `serviceType` é representado no contrato JSON como `"SINGLE"` ou `"COMBO"` e é imutável após criação; cadastro incorreto deve ser desativado e recriado.

SINGLE:

```json
{
  "name": "Corte Masculino",
  "serviceType": "SINGLE",
  "price": 50.00,
  "durationMinutes": 30
}
```

COMBO:

```json
{
  "name": "Corte + Barba",
  "serviceType": "COMBO",
  "price": 80.00,
  "durationMinutes": 60,
  "componentServiceIds": [
    "<corte-id>",
    "<barba-id>"
  ]
}
```

COMBO possui preço/duração próprios. Todos os componentes devem existir, pertencer ao mesmo Business, ser `SINGLE` e não podem ser duplicados. COMBO nunca contém outro COMBO. Violação de nesting retorna `422 COMBO_NESTING_NOT_ALLOWED`. Componentes podem ser alterados enquanto as invariantes forem preservadas; snapshots históricos de AppointmentItem permanecem autoritativos para bookings existentes.

### Disponibilidade de Services por Location

`GET /locations/{locationId}/services`

Retorna os Services associados à Location no contexto do tenant atual.

Uma Location válida sem Services associados retorna `200 OK` com uma coleção vazia:

```json
[]
```

Location inexistente ou pertencente a outro tenant retorna `404 Not Found`, sem revelar a existência de recursos cross-tenant.

`PUT /locations/{locationId}/services`

Substitui o conjunto completo de Services oferecidos pela Location.

Request:

```json
{
  "serviceIds": [
    "<service-id-1>",
    "<service-id-2>"
  ]
}
```

A operação possui semântica de replace completo:
- Services existentes que não estiverem em `serviceIds` são removidos da associação;
- Services já associados e novamente informados são preservados;
- novos Services são associados;
- enviar `serviceIds: []` remove todas as associações;
- repetir o mesmo conjunto produz o mesmo estado final.

Todos os Services devem existir no tenant atual e pertencer ao mesmo Business da Location.

Responses:
- `200 OK`: conjunto de Services substituído com sucesso;
- `400 Bad Request`: request inválido ou `serviceIds` duplicados;
- `404 Not Found`: Location ou Service inexistente no contexto do tenant atual;
- `409 Conflict`: Service pertence a outro Business dentro do mesmo tenant.

`LocationService` representa somente a disponibilidade do Service na unidade. A existência do vínculo significa que o Service está disponível; sua remoção significa que não está disponível.

No MVP, `LocationService`:
- não possui `IsActive`;
- não possui override de preço;
- não possui override de duração.

Preço e duração continuam sendo definidos pelo `Service`.

## 4. Professionals, Locations e Services
`GET /professionals?active=true&locationId=<uuid>&serviceIds=<id1>&serviceIds=<id2>`
`POST /professionals`, `PUT /professionals/{professionalId}`.

Professional pertence ao Business.

`PUT /professionals/{professionalId}/locations`
```json
{"locationIds":["<moema-id>","<tatuape-id>"]}
```
### ProfessionalLocations — implementação validada em 06/10/2026

As rotas implementadas usam o prefixo `/api/v1`.

`GET /professionals/{professionalId}/locations`

Retorna `200 OK` com uma lista de `LocationResponse`, ordenada por nome e ID.
Inclui apenas vínculos ativos. O campo `isActive` da resposta indica o estado
da própria Location; uma Location inativa com vínculo ativo também é retornada.
Professional existente sem vínculos ativos retorna `[]`.
Professional inexistente ou pertencente a outro tenant retorna `404 Not Found`.

`PUT /professionals/{professionalId}/locations`

Recebe `locationIds` e substitui o conjunto de vínculos ativos:

- Cria os vínculos ainda inexistentes.
- Reativa os vínculos selecionados que estavam inativos.
- Desativa os vínculos não selecionados, preservando os registros.
- Preserva `createdAt` dos vínculos existentes.
- Altera `updatedAt` somente quando o estado do vínculo muda.
- Uma lista vazia desativa todos os vínculos do Professional.

Todas as Locations devem pertencer ao mesmo Business do Professional.
As validações ocorrem antes de alterar os vínculos.

| Resultado | HTTP |
| --- | --- |
| Substituição concluída, sem corpo de resposta | 200 |
| `locationIds` ausente, null, com GUID vazio ou IDs duplicados | 400 |
| Professional ou Location inexistente ou de outro tenant | 404 |
| Location de outro Business do mesmo tenant | 409 |

O header `X-Tenant-Id` é temporário para desenvolvimento.
A gestão dos vínculos não substitui a consulta de elegibilidade nem a futura busca de disponibilidade de Scheduling.

### ProfessionalServices — validados em 08/10/2026

As rotas usam o prefixo `/api/v1`.

`GET /professionals/{professionalId}/services`

Retorna `200` com uma lista de `ServiceResponse`, ordenada por nome e ID,
incluindo os componentes dos COMBOs na ordem definida.
Inclui apenas vínculos ativos; Services inativos com vínculo ativo também
são retornados. `isActive` indica o estado do próprio Service.
Professional sem vínculos ativos retorna `[]`.
Professional inexistente ou de outro tenant retorna `404`.

`PUT /professionals/{professionalId}/services`

Corpo: `{"serviceIds":["<corte-id>","<barba-id>"]}`.

Cria vínculos inexistentes, reativa os selecionados e desativa os não selecionados.
Preserva os registros e `createdAt`; altera `updatedAt` apenas quando o estado muda.
Uma lista vazia desativa todos os vínculos.
Todos os Services devem pertencer ao mesmo Business do Professional.
As validações ocorrem antes de alterar os vínculos.

| Resultado | HTTP |
| --- | --- |
| Substituição concluída, sem corpo | 200 |
| `serviceIds` ausente, null, com GUID vazio ou IDs duplicados | 400 |
| Professional ou Service inexistente ou de outro tenant | 404 |
| Service de outro Business do mesmo tenant | 409 |

### Filtros de Professionals — validados em 08/10/2026

- `active` é opcional; quando ausente, inclui profissionais ativos e inativos.
- `locationId` exige Location ativa, do mesmo Business, com vínculo ProfessionalLocation ativo.
- Todos os Services devem estar ativos, pertencer ao mesmo Business e possuir vínculo ProfessionalService ativo.
- Com `locationId`, todos os Services também devem estar vinculados à Location.
- Envie vários Services repetindo o parâmetro `serviceIds`.
- O resultado é ordenado por nome e ID.
- Recursos inexistentes, de outro tenant ou sem elegibilidade retornam `200` com `[]`.
- GUIDs inválidos ou vazios, Services duplicados e `active` inválido retornam `400`.

COMBO exige vínculo explícito; seus componentes não conferem elegibilidade automaticamente.
Use `active=true` para retornar apenas profissionais ativos.
A disponibilidade de horários ainda depende de Scheduling.

`ProfessionalLocation` define onde trabalha; `ProfessionalService` define o que executa. Quando vários serviceIds forem informados, retornar somente profissionais da Location habilitados para TODOS os Services. Para COMBO, elegibilidade é explícita em `ProfessionalService`; não é inferida pelos componentes.

## 5. Availability Rules

Implementação validada em 09/10/2026. As rotas usam o prefixo `/api/v1`.
O header `X-Tenant-Id` é temporário para desenvolvimento.

AvailabilityRule define janelas semanais de um Professional em uma Location.
Os horários são locais, interpretados conforme a timezone da Location.

### Consulta

`GET /locations/{locationId}/professionals/{professionalId}/availability-rules`

Retorna `200` com as regras ativas, ordenadas por dia da semana,
início, fim e ID. Sem regras ativas, retorna `[]`.

Exemplo de resposta:

```json
[
  {
    "id": "<rule-id>",
    "dayOfWeek": 1,
    "startTime": "09:00:00",
    "endTime": "12:00:00"
  }
]
```

Location e Professional devem existir no tenant atual, pertencer ao mesmo
Business e possuir vínculo ProfessionalLocation.
Caso contrário, a consulta retorna `404`.

A consulta administrativa permite ler regras mesmo quando Location,
Professional ou ProfessionalLocation estão inativos.
Isso não significa disponibilidade para agendamento.

### Substituição

`PUT /locations/{locationId}/professionals/{professionalId}/availability-rules`

```json
{
  "rules": [
    {"dayOfWeek": 1, "startTime": "09:00", "endTime": "12:00"},
    {"dayOfWeek": 1, "startTime": "13:00", "endTime": "18:00"}
  ]
}
```

- `rules` é obrigatório; uma lista vazia desativa todas as regras do par.
- Cada regra exige `dayOfWeek`, `startTime` e `endTime`.
- `dayOfWeek` usa números de 0 (domingo) a 6 (sábado).
- Domingo e início à meia-noite (`00:00`) são aceitos.
- O início deve ser anterior ao fim, dentro do mesmo dia.
- Janelas que atravessam a meia-noite não são aceitas nesta entrega.
- São permitidas várias janelas por dia, sem sobreposição.
- Janelas adjacentes são aceitas; janelas duplicadas são rejeitadas.
- Location, Professional e ProfessionalLocation devem estar ativos.
- Location e Professional devem pertencer ao mesmo Business.
- As validações ocorrem antes de alterar a agenda.

A substituição cria janelas inexistentes, reativa as selecionadas e
desativa as omitidas, preservando os registros.
A mesma combinação de dia, início e fim preserva o ID e `createdAt`.
`updatedAt` muda somente quando o estado da regra muda.
Alterar os horários desativa a janela anterior e cria ou reativa outra.

| Resultado do PUT | HTTP |
| --- | --- |
| Substituição concluída, sem corpo de resposta | 200 |
| Payload incompleto, dia inválido, janela inválida ou sobreposição | 400 |
| Location, Professional ou vínculo inexistente ou de outro tenant | 404 |
| Professional de outro Business do mesmo tenant | 409 |
| Location, Professional ou vínculo inativo | 409 |

GUIDs vazios nos parâmetros de rota retornam `400` no GET e no PUT.

Esta entrega gerencia regras semanais. A busca de slots, os bloqueios
e os conflitos com agendamentos serão implementados nas próximas etapas.


## 6. Schedule Blocks

Implementação incremental validada em 14/10/2026.
As rotas usam o prefixo `/api/v1`.
O header `X-Tenant-Id` é temporário para desenvolvimento.

### Criação

`POST /schedule-blocks`

Requer o header `Idempotency-Key`.

```json
{
  "locationId": "<location-id>",
  "professionalId": null,
  "startsAt": "2026-10-19T10:00:00-03:00",
  "endsAt": "2026-10-19T10:30:00-03:00"
}
```

LocationId, StartsAt e EndsAt são obrigatórios.
ProfessionalId ausente ou null bloqueia toda a unidade.
Quando preenchido, bloqueia somente o profissional naquela unidade.

O início deve preceder o fim. Bloqueios podem atravessar dias.
Os instantes são normalizados para UTC.
A sobreposição usa intervalos [StartsAt, EndsAt): encostar no limite
não constitui conflito.

Location e Business devem estar ativos.
Quando informado, Professional deve estar ativo, pertencer ao mesmo
Business da Location e possuir ProfessionalLocation ativo.

Retorna `201` com Id, LocationId, ProfessionalId, StartsAt, EndsAt,
IsActive, CreatedAt e UpdatedAt.

### Idempotência

- Identidade: TenantId + operação CreateScheduleBlock + chave.
- Chave obrigatória, com até 200 caracteres, sem espaços ou caracteres de controle.
- A chave é preservada e diferencia maiúsculas de minúsculas.
- Mesma chave e conteúdo retornam `201` com a resposta original.
- Offsets diferentes que representam os mesmos instantes são equivalentes.
- Mesma chave e conteúdo diferente retornam `409`,
  com `code: IDEMPOTENCY_CONFLICT`.
- Bloqueio e resultado idempotente são gravados na mesma transação.
- Requisições concorrentes com a mesma chave não duplicam o bloqueio.
- Após desativação, replay não reativa o bloqueio:
  devolve a resposta original da criação, inclusive seu IsActive original.
- Falhas de validação não persistem bloqueio nem registro idempotente.

### Consulta

`GET /schedule-blocks?locationId=<uuid>&from=<instant>&to=<instant>&professionalId=<uuid>`

LocationId, from e to são obrigatórios; from deve preceder to.
Retorna `200` com bloqueios ativos que se sobrepõem ao período,
ordenados por StartsAt e Id. Sem resultados, retorna `[]`.

Sem ProfessionalId, inclui todos os escopos da unidade.
Com ProfessionalId, inclui os bloqueios desse profissional e os
bloqueios da unidade inteira.

A consulta permite Location inativa para leitura administrativa.
Location inexistente ou de outro tenant retorna `404`.

### Desativação

`DELETE /schedule-blocks/{blockId}`

Retorna `204` para bloqueio existente, inclusive já inativo.
Preserva o registro e CreatedAt; altera UpdatedAt somente na
primeira desativação.
Bloqueio inexistente ou de outro tenant retorna `404`.
GUID vazio retorna `400`.

A chave idempotente da criação permanece preservada.

### Resultados da criação

| Resultado | HTTP |
| --- | --- |
| Criação ou replay da resposta original | 201 |
| Payload, intervalo, IDs ou chave inválidos | 400 |
| Location, Business, Professional ou vínculo inexistente ou de outro tenant | 404 |
| Recursos ou vínculo inativos; conflito de Business | 409 |
| Mesma chave com conteúdo diferente | 409 |

### Integração com slots e limite da entrega

A busca de slots descarta candidatos cujo intervalo completo se
sobreponha a bloqueios ativos aplicáveis.
Bloqueios da unidade afetam todos os profissionais; bloqueios
específicos afetam somente o profissional indicado.
A grade original de inícios é preservada.

`POST /schedule-blocks/impact` permanece previsto e ainda não implementado.
A avaliação de impacto depende de Appointments.
Criar um bloqueio não move nem cancela agendamentos automaticamente.


## 7. Customers

Cliente final pertencente a um Business e ao tenant atual.
Telefone/WhatsApp é identificador operacional principal, mas não PK.

Implementado em 15/10/2026 com nome opcional, telefone obrigatório,
e-mail opcional e datas de criação e atualização em UTC.

A Application normaliza o telefone para `+` seguido de dígitos ASCII,
sem inferir país. A validação é estrutural e não confirma existência
do número nem disponibilidade no WhatsApp.
Nome e e-mail em branco são armazenados como null.

A criação exige Business ativo do tenant atual.
Consultas e escritas respeitam o isolamento entre tenants.
Telefone não possui restrição de unicidade nesta entrega.

A API expõe criação, listagem e consulta por ID.
Evitar enriquecer Customer com dados pessoais sem finalidade definida.

### Criação

`POST /api/v1/customers`

```json
{
  "businessId": "<business-id>",
  "phone": "+55 (11) 99999-0001",
  "name": "Maria Silva",
  "email": "maria@example.com"
}
```

O Business deve existir no tenant atual e estar ativo.

Regras de telefone:
- Exige prefixo internacional explícito com `+`.
- Aceita de 1 a 15 dígitos ASCII, com primeiro dígito diferente de zero.
- Remove espaços nas extremidades e ignora espaços comuns, parênteses,
  hífens e pontos na parte após o `+`.
- Não infere país nem acrescenta código de país.
- Rejeita letras, ramais e demais caracteres.
- A validação é estrutural; não confirma existência do número,
  validade do plano de numeração ou disponibilidade no WhatsApp.

Nome e e-mail têm espaços nas extremidades removidos.
Valores ausentes, null ou em branco são armazenados como null.
Quando informado, o e-mail deve representar um endereço simples,
sem nome de apresentação, lista de destinatários ou caracteres de controle.
Não há verificação de existência da caixa postal.

Retorna `201` com o Customer criado e o header
`Location: /api/v1/customers/{id}`.

```json
{
  "id": "<customer-id>",
  "businessId": "<business-id>",
  "name": "Maria Silva",
  "phone": "+5511999990001",
  "email": "maria@example.com"
}
```

Telefone não é PK e não possui restrição de unicidade nesta entrega.
Repetir uma criação válida pode gerar outro Customer.

| Resultado do POST | HTTP |
| --- | --- |
| Customer criado | 201 |
| BusinessId ausente, vazio ou malformado; telefone ou e-mail inválido | 400 |
| Business inexistente ou de outro tenant | 404 |
| Business inativo | 409 |

### Listagem

`GET /api/v1/customers`

Retorna `200` com os Customers do tenant atual, ordenados por nome e ID.
A listagem pode incluir Customers de diferentes Businesses desse tenant.
Sem registros, retorna `[]`.

### Consulta por ID

`GET /api/v1/customers/{customerId}`

Retorna `200` com o mesmo formato de Customer apresentado na criação.
Customer inexistente ou de outro tenant retorna `404`.
GUID vazio retorna `400`.

### Limite desta entrega

Inclui criação, listagem e consulta por ID.
Atualização e exclusão de Customers não estão expostas pela API.

## 8. Booking Quote

Implementação validada em 16/10/2026.
O header `X-Tenant-Id` é temporário para desenvolvimento.

`POST /api/v1/booking/quote`

Calcula preço e duração dos serviços selecionados na unidade.
A cotação é informativa, não é persistida e não reserva horário.

### Entrada

```json
{
  "locationId": "<location-id>",
  "serviceIds": ["<corte-id>", "<barba-id>"]
}
```

- LocationId e pelo menos um ServiceId são obrigatórios.
- IDs vazios, malformados ou duplicados são rejeitados.
- Location e Business devem estar ativos no tenant atual.
- Todos os Services devem estar ativos, pertencer ao Business da
  Location e ser oferecidos nela via LocationService.

### Resposta

Retorna `200` com os itens na ordem dos ServiceIds solicitados
e os totais calculados no backend.

```json
{
  "locationId": "<location-id>",
  "items": [
    {
      "serviceId": "<corte-id>",
      "name": "Corte masculino premium",
      "price": 65.00,
      "durationMinutes": 45
    },
    {
      "serviceId": "<barba-id>",
      "name": "Barba",
      "price": 35.00,
      "durationMinutes": 20
    }
  ],
  "totalPrice": 100.00,
  "totalDurationMinutes": 65
}
```

### Regras de cálculo

- Preço e duração vêm dos Services, sem override por Location.
- Os totais são a soma dos valores dos serviços selecionados.
- COMBO usa preço e duração próprios, sem expandir seus componentes.
- Oferecer os componentes na unidade não oferece o COMBO automaticamente:
  ele exige seu próprio vínculo LocationService.
- Não há substituição automática de serviços individuais por COMBO.
- Preço total zero é aceito.
- Overflow de preço ou duração total acima de int.MaxValue é rejeitado.
- Cada consulta usa os valores atuais do catálogo.
- A cotação não avalia profissional, agenda, bloqueios ou ocupação.

| Resultado | HTTP |
| --- | --- |
| Cotação calculada | 200 |
| Payload incompleto, IDs vazios, malformados ou duplicados | 400 |
| Location, Business ou Service inexistente ou de outro tenant | 404 |
| Location ou Business inativo | 409 |
| Service inativo, de outro Business ou não oferecido na unidade | 409 |
| Totais inválidos ou acima dos limites suportados | 409 |

CreateAppointment deverá revalidar os recursos e recalcular os valores.
A cotação não garante preço futuro nem disponibilidade para agendamento.

## 9. Available Slots

Implementação incremental validada em 13/10/2026.

`POST /api/v1/availability/slots/search`

O header `X-Tenant-Id` é temporário para desenvolvimento.

### Entrada

```json
{
  "locationId": "<location-id>",
  "serviceIds": ["<corte-id>", "<barba-id>"],
  "date": "2026-10-19",
  "professionalId": null
}
```

- LocationId, date e pelo menos um ServiceId são obrigatórios.
- IDs vazios e ServiceIds duplicados são rejeitados.
- ProfessionalId é opcional; ausente ou null busca todos os elegíveis.
- Date representa a data local na timezone da Location.

### Resposta

Retorna `200` com timezone, duração total e slots ordenados por
StartsAt e ProfessionalId.

```json
{
  "locationId": "<location-id>",
  "date": "2026-10-19",
  "timezone": "America/Sao_Paulo",
  "totalDurationMinutes": 65,
  "slots": [
    {
      "professionalId": "<professional-id>",
      "startsAt": "2026-10-19T12:00:00+00:00",
      "endsAt": "2026-10-19T13:05:00+00:00"
    }
  ]
}
```

StartsAt e EndsAt são instantes UTC.
Sem profissionais elegíveis ou janelas disponíveis, retorna `slots: []`.
Datas passadas também retornam lista vazia.

### Regras implementadas

- Location e Business devem estar ativos no tenant atual.
- Todos os Services devem estar ativos, pertencer ao Business da Location
  e ser oferecidos nela via LocationService.
- Professional deve estar ativo, com ProfessionalLocation ativo e
  ProfessionalService ativo para todos os Services solicitados.
- COMBO exige habilitação explícita e usa sua própria duração,
  sem somar novamente os componentes.
- A duração total é a soma das durações dos Services selecionados.
- Apenas AvailabilityRules ativas do dia e da unidade são consideradas.
- Janelas adjacentes ou sobrepostas são unidas para formar intervalos contínuos.
- Descansos entre janelas são preservados.
- Os inícios avançam conforme Business.SlotIntervalMinutes,
  a partir do início de cada janela contínua.
- O atendimento completo deve caber na janela.
- Inícios iguais ou anteriores ao instante da consulta são excluídos.
- Horários locais com início ou fim inexistente ou ambíguo por mudança
  de timezone são descartados.
- Candidatos cuja duração em UTC difere da duração local são descartados.

| Resultado | HTTP |
| --- | --- |
| Busca concluída, inclusive sem slots | 200 |
| Payload incompleto, IDs vazios ou duplicados, GUID ou data malformados | 400 |
| Location, Business ou Service inexistente ou de outro tenant | 404 |
| Location ou Business inativo | 409 |
| Service inativo, de outro Business ou não oferecido na Location | 409 |
| Duração total inválida | 409 |
| Timezone configurada na Location não resolvida pelo servidor | 500 |

Professional informado sem elegibilidade, inexistente ou de outro tenant
não produz slots; a busca retorna `200` com lista vazia, desde que
Location e Services sejam válidos.

### Limite desta entrega

A busca considera elegibilidade, agenda semanal, ScheduleBlocks ativos
e Appointments nos estados que ocupam agenda.
A ocupação considera o profissional em todas as unidades do tenant.
Slots não são persistidos e não constituem reserva nem lock.

O fluxo de disponibilidade implementado é:
LocationService -> ProfessionalLocation -> ProfessionalService ->
AvailabilityRule -> ScheduleBlock -> Appointments.

## 10. Appointments

### Implementação incremental — atualizada em 20/10/2026

Criação e consulta por ID foram validadas em 16/10/2026.
Listagem paginada foi validada em 19/10/2026.

Rotas implementadas:
- `POST /api/v1/appointments`
- `GET /api/v1/appointments`
- `GET /api/v1/appointments/{appointmentId}`
- `POST /api/v1/appointments/{appointmentId}/cancel`
- `POST /api/v1/appointments/{appointmentId}/reschedule`

O header `X-Tenant-Id` é temporário para desenvolvimento.
Cancelamento idempotente foi validado em 19/10/2026.
Reagendamento idempotente foi validado em 20/10/2026.
Confirmação por pagamento permanece pendente.

#### Listagem paginada

`GET /api/v1/appointments`

Todos os filtros são opcionais e combinados com AND.

| Parâmetro | Regra |
| --- | --- |
| locationId | GUID não vazio |
| professionalId | GUID não vazio |
| customerId | GUID não vazio |
| status | Nome público do estado, em maiúsculas |
| from | Início do período; exige to |
| to | Fim do período; exige from e deve ser posterior a from |
| page | Inteiro a partir de 1; padrão 1 |
| pageSize | Inteiro entre 1 e 100; padrão 50 |

Estados aceitos: `PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`,
`RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`,
`COMPLETED`, `NO_SHOW` e `EXPIRED`.
Valores numéricos, vazios ou com outra capitalização são rejeitados.

Datas são normalizadas para UTC.
O filtro de período usa sobreposição:
`StartsAt < to && from < EndsAt`.
Agendamentos que terminam exatamente em from ou começam exatamente
em to ficam fora do resultado.

Exemplo:

`GET /api/v1/appointments?status=EXPIRED&page=1&pageSize=5`

Resposta sem registros:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 5,
  "totalCount": 0
}
```

Cada elemento de items usa o mesmo formato da consulta por ID,
incluindo estado atual, auditoria e snapshots comerciais.
Os itens comerciais de cada agendamento são ordenados por ServiceId.

Os agendamentos são ordenados por StartsAt crescente e depois por Id.
TotalCount considera todos os registros após os filtros, antes da paginação.
Página além do resultado retorna items vazio, preservando TotalCount.

Itens e contagem consideram somente o tenant atual.
Filtros com IDs inexistentes ou de outro tenant retornam lista vazia.
A consulta não exige que os recursos históricos estejam ativos.
Sem filtro de status, todos os estados podem ser retornados.
A leitura não altera estados nem materializa expiração.

Contagem e página são lidas em consultas separadas; alterações concorrentes
podem ser observadas entre essas leituras.

| Resultado | HTTP |
| --- | --- |
| Consulta válida, com ou sem registros | 200 |
| GUID, status, período ou paginação inválidos | 400 |
| Deslocamento calculado da página acima de int.MaxValue | 400 |

#### Criação e idempotência

A criação exige `Idempotency-Key`, com até 200 caracteres,
sem espaços ou caracteres de controle.

CustomerId, LocationId, ProfessionalId, StartsAt e pelo menos
um ServiceId são obrigatórios. IDs vazios ou duplicados são rejeitados.
StartsAt deve representar um instante com precisão máxima de microssegundos.

O backend valida:
- Location e Business ativos;
- Customer pertencente ao Business da Location;
- Professional ativo e do mesmo Business;
- ProfessionalLocation existente e ativo;
- todos os Services ativos, do mesmo Business e oferecidos na unidade;
- ProfessionalService ativo para todos os serviços, inclusive COMBO;
- início futuro, pertencente à grade da agenda;
- intervalo completo disponível, respeitando timezone e bloqueios;
- ausência de Appointment ocupante do profissional em qualquer unidade
  do tenant.

Appointment, itens e resposta idempotente são persistidos atomicamente.
Preço e duração são calculados no backend e preservados em snapshots.
COMBO usa seus próprios valores, sem expandir componentes.

Criação e replay retornam `201`, com o header
`Location: /api/v1/appointments/{id}`.

Exemplo de resposta:

```json
{
  "id": "<appointment-id>",
  "businessId": "<business-id>",
  "locationId": "<location-id>",
  "customerId": "<customer-id>",
  "professionalId": "<professional-id>",
  "startsAt": "2026-10-19T12:00:00+00:00",
  "endsAt": "2026-10-19T12:45:00+00:00",
  "status": "PENDING",
  "totalPrice": 65.00,
  "totalDurationMinutes": 45,
  "reservationExpiresAt": "2026-10-16T22:48:05+00:00",
  "items": [
    {
      "serviceId": "<service-id>",
      "name": "Corte",
      "price": 65.00,
      "durationMinutes": 45
    }
  ],
  "createdAt": "2026-10-16T22:38:05+00:00",
  "updatedAt": "2026-10-16T22:38:05+00:00"
}
```

Mesma chave e conteúdo equivalente retornam a resposta original.
Offsets diferentes para o mesmo instante são equivalentes.
A ordem de ServiceIds faz parte do conteúdo da requisição.
Mesma chave com conteúdo diferente retorna `409 IDEMPOTENCY_CONFLICT`.

Replay não revalida o catálogo nem reativa reservas expiradas.
Pode retornar PENDING na resposta original mesmo quando o estado atual
já é EXPIRED; consulte o recurso por ID para obter o estado atual.

#### Consulta por ID

Retorna `200` com o estado e os snapshots persistidos.
Os itens são ordenados por ServiceId.
Appointment inexistente ou de outro tenant retorna `404`.
GUID vazio retorna `400`.

#### Resultados da criação

| Resultado | HTTP |
| --- | --- |
| Criação ou replay concluído | 201 |
| Entrada ou chave de idempotência inválida | 400 |
| Recurso ou ProfessionalLocation inexistente ou de outro tenant | 404 |
| Horário indisponível | 409, code SLOT_UNAVAILABLE |
| Mesma chave com conteúdo diferente | 409, code IDEMPOTENCY_CONFLICT |
| Recursos inativos, inelegíveis, de Business incompatível ou totais inválidos | 409, code APPOINTMENT_CONFLICT |
| Timezone não resolvida pelo servidor | 500 |

Os códigos de conflito são retornados no campo `code` de Problem Details.

#### Expiração implementada

A configuração usa a seção `Scheduling:Reservations`:
- `LifetimeMinutes`: padrão 10, inteiro positivo;
- `ExpirationWorkerEnabled`: padrão false;
- `ExpirationIntervalSeconds`: padrão 30, inteiro positivo.

A expiração da reserva é limitada ao início do atendimento.
O worker exige ativação explícita e executa ciclos em escopos separados
por tenant, alterando somente PENDING com prazo vencido para EXPIRED.
A repetição preserva UpdatedAt dos registros já expirados.

A agenda é liberada quando a mudança para EXPIRED é persistida.
Enquanto permanecer PENDING, o registro continua ocupando o intervalo,
mesmo após o prazo.
A confirmação por pagamento ainda não está implementada nesta entrega.

`GET /api/v1/appointments?locationId=<uuid>`
`GET /api/v1/appointments/{appointmentId}`

`POST /appointments` requer `Idempotency-Key`.
```json
{
  "customerId":"<uuid>",
  "locationId":"<uuid>",
  "professionalId":"<uuid>",
  "serviceIds":["<corte-id>","<barba-id>"],
  "startsAt":"2026-09-20T14:00:00-03:00"
}
```

Create não aceita preço, duração, `endsAt` ou status autoritativos. Backend revalida Customer, Location, Services na Location, ProfessionalLocation, ProfessionalServices, AvailabilityRules, ScheduleBlocks, Appointments e concorrência; calcula preço/duração/EndsAt e cria Appointment + AppointmentItems com snapshots comerciais.

Resposta de criação usa `201 Created`, retorna `Location` do recurso e os valores derivados/snapshots, incluindo `status=PENDING`, `totalPrice`, `totalDurationMinutes`, `reservationExpiresAt` e items.

### Pagamento integral obrigatório e expiração
No MVP não existe sinal, adiantamento, pagamento parcial nem reservar para pagar depois. Todo Appointment criado para reserva comercial inicia `PENDING`, ocupa agenda e somente se torna `CONFIRMED` após pagamento integral confirmado pelo provider.

`Payment.Amount == Appointment.TotalPriceSnapshot` para pagamento de serviço. O tempo de reserva é configurável; não é hardcoded no contrato. Ao atingir `reservationExpiresAt` sem pagamento confirmado, worker materializa `EXPIRED`, liberando o slot.

Fluxo: `PENDING -> Payment -> PaymentAttempt -> webhook confirmado -> Payment CONFIRMED -> Appointment CONFIRMED`.

`CONFIRMED_BY_CLIENT` representa confirmação posterior de presença pelo cliente, processada pelo backend após interpretação do canal; IA não altera estado diretamente.

### Double-booking
`409 SLOT_UNAVAILABLE`. Tentativa perdedora não persiste Appointment. A proteção considera o Professional globalmente dentro do Tenant, evitando reserva simultânea em duas Locations. Idempotência não substitui a exclusion constraint do PostgreSQL.

### Reagendamento — implementação em 20/10/2026

`POST /api/v1/appointments/{appointmentId}/reschedule`

Exige `Idempotency-Key` não vazia, com até 200 caracteres,
sem espaços ou caracteres de controle.

Entrada:

```json
{
  "locationId": "<location-id>",
  "professionalId": "<professional-id>",
  "startsAt": "2026-10-26T10:00:00-03:00"
}
```

Os três campos são obrigatórios, inclusive quando unidade ou profissional
permanecem os mesmos. Os IDs devem ser GUIDs não vazios.
StartsAt é normalizado para UTC e deve ter precisão de microssegundos.

O backend revalida recursos ativos, Business, serviços oferecidos na
unidade, vínculo e elegibilidade do profissional, agenda semanal,
grade de início, timezone, bloqueios e conflitos com outros agendamentos.
O intervalo do próprio Appointment é excluído da consulta de conflitos.
A ocupação do profissional considera todas as unidades do tenant.

Podem ser reagendados PENDING, CONFIRMED, CONFIRMED_BY_CLIENT
e RESCHEDULE_REQUESTED, desde que o atendimento original ainda não
tenha começado. O novo início deve estar no futuro.
PENDING também exige prazo de reserva vigente.

Preserva AppointmentId, TenantId, BusinessId, CustomerId, itens,
snapshots comerciais, CreatedAt e estado operacional.
Não altera a composição dos serviços nem recalcula preço e duração
pelo catálogo atual. EndsAt é derivado da duração persistida.

A unidade de destino deve pertencer ao Business original.
Se uma evolução futura exigir ajuste financeiro, o MVP usará
cancelamento e novo booking, sem cobrança complementar ou refund parcial.

Para PENDING, ReservationExpiresAt nunca é estendido.
Quando o novo início antecede o prazo vigente, a expiração é reduzida
para esse início.

Retorna 200 com AppointmentResponse, no mesmo formato da consulta por ID,
incluindo novo intervalo, unidade, profissional e snapshots preservados.
Não cria estado permanente RESCHEDULED.

A alteração e a resposta idempotente são persistidas atomicamente.
Após o commit, o intervalo anterior deixa de ocupar agenda e o novo
passa a ocupá-la, conforme o estado do Appointment.
Falhas de reagendamento preservam o intervalo original, exceto quando
a tentativa materializa a expiração de uma reserva vencida.

Idempotência:

- Escopo: TenantId + Operation RescheduleAppointment + IdempotencyKey.
- Hash considera AppointmentId, LocationId, ProfessionalId e início em UTC.
- Mesma chave e conteúdo retornam a resposta original.
- Offsets que representam o mesmo instante são equivalentes.
- Mesma chave com conteúdo diferente retorna IDEMPOTENCY_CONFLICT.
- Replay não reaplica a alteração nem reativa reservas expiradas ou canceladas.
- Uma solicitação sem mudança efetiva preserva UpdatedAt e registra
  a resposta idempotente após revalidar as condições da operação.

PENDING vencido é materializado como EXPIRED e retorna
RESERVATION_EXPIRED, sem registrar sucesso idempotente de reagendamento.
Um registro já EXPIRED também retorna esse código quando o relógio
da operação é compatível com sua última atualização.

Atualizações concorrentes usam os valores originais dos tokens
configurados no EF Core. A exclusion constraint do PostgreSQL
continua sendo a proteção contra sobreposição de horários.

| Resultado | HTTP | Código |
| --- | --- | --- |
| Reagendamento ou replay concluído | 200 | — |
| Entrada ou chave inválida | 400 | — |
| Agendamento ou recurso de referência não encontrado no tenant | 404 | — |
| Estado incompatível ou atendimento original já iniciado | 409 | INVALID_APPOINTMENT_STATE |
| Reserva expirada | 409 | RESERVATION_EXPIRED |
| Destino indisponível | 409 | SLOT_UNAVAILABLE |
| Recursos inativos ou incompatíveis | 409 | APPOINTMENT_CONFLICT |
| Chave reutilizada com outro conteúdo | 409 | IDEMPOTENCY_CONFLICT |
| Disputa de atualização ou relógio anterior à última atualização | 409 | CONCURRENCY_CONFLICT |
| Timezone configurada inválida | 500 | — |

As consultas retornam o estado atual; o replay retorna a resposta
original daquela operação. Histórico de transições, publicação de eventos
e reagendamento assistido permanecem para próximas entregas.

### Cancelamento — implementação em 19/10/2026

`POST /api/v1/appointments/{appointmentId}/cancel`

Exige `Idempotency-Key` com até 200 caracteres, sem espaços
ou caracteres de controle.

Entrada:

```json
{
  "cancelledBy": "CLIENT",
  "reason": "Cliente solicitou"
}
```

- AppointmentId deve ser um GUID não vazio.
- cancelledBy aceita exatamente CLIENT ou BUSINESS.
- reason é obrigatório e recebe Trim antes da validação de tamanho.
- O motivo normalizado deve conter entre 1 e 500 caracteres.
- O tenant é resolvido pelo mecanismo de contexto atual.
- cancelledBy informa a origem operacional; não representa autenticação
  nem autorização do solicitante. Essas proteções permanecem pendentes.

Retorna 200 com a auditoria do cancelamento:

```json
{
  "id": "<appointment-id>",
  "status": "CANCELLED_BY_CLIENT",
  "cancellationReason": "Cliente solicitou",
  "cancelledAt": "2026-10-19T23:07:00.236501+00:00",
  "updatedAt": "2026-10-19T23:07:00.236501+00:00"
}
```

BUSINESS resulta em CANCELLED_BY_BUSINESS.
Datas são persistidas em UTC, com precisão de microssegundos.

Podem ser cancelados os estados PENDING, CONFIRMED,
CONFIRMED_BY_CLIENT e RESCHEDULE_REQUESTED.
PENDING exige que o prazo da reserva ainda não tenha vencido.
COMPLETED, NO_SHOW e cancelamento pela outra origem não permitem
a operação. EXPIRED retorna RESERVATION_EXPIRED.

A transação persiste a alteração do Appointment e a resposta idempotente
atomicamente. O intervalo deixa de ocupar agenda após o commit.
Itens, snapshots comerciais e prazo original da reserva são preservados.
O cancelamento não depende de recursos do catálogo continuarem ativos.

Idempotência:
- Escopo: TenantId + Operation CancelAppointment + IdempotencyKey.
- Hash considera AppointmentId, origem e motivo normalizado.
- Mesma chave e conteúdo retornam a resposta original.
- Mesma chave com conteúdo diferente retorna IDEMPOTENCY_CONFLICT.
- Nova chave para a mesma origem e motivo preserva a auditoria original
  e registra a resposta para a nova chave.
- Nova chave com outra origem ou motivo em agendamento já cancelado
  retorna INVALID_APPOINTMENT_STATE.

Ao tentar cancelar PENDING vencido, o backend materializa EXPIRED
e retorna RESERVATION_EXPIRED, sem registrar resposta idempotente
de cancelamento. Um registro já EXPIRED também retorna esse conflito.

Atualizações concorrentes são protegidas pelo token de Status.
Quando outra operação vence a disputa, o cancelamento retorna
CONCURRENCY_CONFLICT e sua transação é revertida.
O cliente pode consultar o estado atual e repetir a solicitação
com a mesma chave.

| Resultado | HTTP | Código |
| --- | --- | --- |
| Cancelamento ou replay concluído | 200 | — |
| Entrada ou chave inválida | 400 | — |
| Agendamento inexistente ou de outro tenant | 404 | — |
| Estado ou dados de cancelamento incompatíveis | 409 | INVALID_APPOINTMENT_STATE |
| Reserva expirada | 409 | RESERVATION_EXPIRED |
| Chave reutilizada com outro conteúdo | 409 | IDEMPOTENCY_CONFLICT |
| Disputa de atualização | 409 | CONCURRENCY_CONFLICT |

A resposta de cancelamento contém motivo e data.
As consultas gerais de Appointments retornam o status atualizado;
seu DTO atual não inclui os campos de auditoria do cancelamento.

Esta entrega não solicita nem confirma refund.
Política financeira, confirmação por pagamento e estornos permanecem
para a integração de Payments e Refunds.


Estados operacionais previstos: `PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `COMPLETED`, `NO_SHOW`, `EXPIRED`. Reschedule não cria estado permanente `RESCHEDULED`; mantém histórico/evento e retorna ao estado operacional apropriado.

## 11. Payments e PaymentAttempts
Payment representa a obrigação financeira lógica do Appointment; PaymentAttempt representa cada tentativa concreta no provider. Um Payment pode possuir N PaymentAttempts.

Criar nova tentativa:
```http
POST /appointments/{appointmentId}/payment-attempts
Idempotency-Key: <uuid>
```
```json
{"paymentMethod":"PIX"}
```

Métodos permitidos para pagamento de serviço: `PIX`, `CREDIT_CARD`, `DEBIT_CARD`. Sem sinal, parcial ou boleto. O cliente nunca envia o valor. Backend valida Appointment `PENDING` e não expirado, obtém/cria o único Payment lógico SERVICE do Appointment, garante `Payment.Amount == Appointment.TotalPriceSnapshot` e cria a PaymentAttempt.

Resposta de criação da tentativa usa `201 Created` e pode retornar `paymentId`, `paymentAttemptId`, `paymentMethod`, `status`, `amount`, `checkoutUrl`/referência segura e `expiresAt`. Dados de cartão nunca transitam por IA, WhatsApp ou nossa API; checkout é hospedado/tokenizado pelo provider.

Uma tentativa PIX pode expirar e ser seguida por cartão ou novo PIX sem criar outro Payment. Mesma intenção repetida usa idempotência; nova escolha consciente de pagamento usa nova Idempotency-Key e nova PaymentAttempt.

Estados Payment: `PENDING`, `PROCESSING`, `CONFIRMED`, `FAILED`, `CANCELLED`, `REFUNDED`.

Estados PaymentAttempt: `CREATED`, `PENDING`, `PROCESSING`, `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED`.

Somente PaymentAttempt confirmada por evento/API confiável do provider pode liquidar o Payment. Redirect de browser é informativo e nunca confirma pagamento. Se Payment já estiver confirmado, nova tentativa é rejeitada com `422 PAYMENT_ALREADY_CONFIRMED`.

### Webhook e confirmação financeira
Webhook valida autenticidade/assinatura conforme provider e é deduplicado por `Provider + ProviderEventId`. Processamento é idempotente e atualiza PaymentAttempt -> Payment -> Appointment somente quando a transição for válida.

Se confirmação financeira chegar após Appointment estar `EXPIRED`, a verdade financeira é registrada (`PaymentAttempt CONFIRMED`, `Payment CONFIRMED`), mas Appointment permanece `EXPIRED`. O sistema nunca reativa o slot e dispara Refund compensatório integral.

## 12. Refunds
`POST /payments/{paymentId}/refunds` requer `Idempotency-Key`.
```json
{"reason":"CUSTOMER_CANCELLATION"}
```

Refund é integral no MVP. O cliente não envia `amount`; backend determina `Refund.Amount = Payment.Amount`, que para SERVICE corresponde ao `Appointment.TotalPriceSnapshot`.

Backend valida Tenant, Payment `CONFIRMED`, PaymentAttempt efetivamente `CONFIRMED`, política de cancelamento e inexistência de outro estorno integral efetivo/em processamento incompatível. Refund referencia Payment e a PaymentAttempt que originou a transação externa.

Solicitação aceita retorna `202 Accepted` com estado inicial `REFUND_REQUESTED`. Processamento é assíncrono: `REFUND_REQUESTED -> REFUND_PROCESSING -> REFUNDED | REFUND_FAILED`. Nunca comunicar refund concluído antes da confirmação do provider.

Cancelamento causado pelo estabelecimento segue fluxo de estorno integral quando houver pagamento. Cancelamento pelo cliente respeita política configurada; quando houver direito a estorno, ele continua integral no MVP.

Retries usam a mesma Idempotency-Key e, quando disponível, idempotência do provider. O objetivo é no máximo um estorno integral efetivo, mesmo com timeout, retry ou webhook duplicado.

Pagamento confirmado tardiamente após Appointment `EXPIRED` dispara este mesmo mecanismo como Refund compensatório integral; o Appointment permanece expirado.

## 13. Analytics
Endpoints financeiros aceitam `locationId` opcional para visão por unidade:
`GET /analytics/financial-summary?locationId=<uuid>`
`GET /analytics/revenue-by-professional?locationId=<uuid>`
`GET /analytics/revenue-by-service?locationId=<uuid>`
`GET /analytics/forecast?locationId=<uuid>`

## 14. SaaS Billing
Billing separado de Customer Payments. Subscription associa unidades faturáveis explicitamente via SubscriptionUnit/Location.

Mensal: RECURRING_CARD ou PIX. Anual: RECURRING_CARD. Sem boleto.

## 15. Usage
`GET /usage/summary?locationId=<uuid>` quando o consumo puder ser atribuído a uma unidade. Custos globais continuam tenant-scoped.

## 16. WhatsApp / Conversation
`POST /integrations/whatsapp/webhook`.

```text
WhatsApp Adapter -> Messaging -> Conversation Engine -> AI Gateway -> Application tools -> Domain
```

LLM não acessa EF Core/DB diretamente e não confirma pagamento/refund.

## 17. Códigos de domínio
```text
VALIDATION_ERROR
UNAUTHORIZED
FORBIDDEN
RESOURCE_NOT_FOUND
LOCATION_NOT_AVAILABLE
SERVICE_NOT_AVAILABLE_AT_LOCATION
PROFESSIONAL_NOT_AVAILABLE_AT_LOCATION
PROFESSIONAL_NOT_ELIGIBLE_FOR_SERVICE
INVALID_COMBO
COMBO_COMPONENT_INVALID
COMBO_NESTING_NOT_ALLOWED
SLOT_UNAVAILABLE
APPOINTMENT_NOT_CANCELABLE
APPOINTMENT_NOT_RESCHEDULABLE
APPOINTMENT_EXPIRED
PAID_APPOINTMENT_SERVICE_CHANGE_NOT_ALLOWED
PAYMENT_NOT_ALLOWED
PAYMENT_ALREADY_CONFIRMED
PAYMENT_PROVIDER_ERROR
REFUND_NOT_ALLOWED
REFUND_ALREADY_COMPLETED
IDEMPOTENCY_CONFLICT
RATE_LIMIT_EXCEEDED
INTERNAL_ERROR
```

## 18. Primeira fatia vertical
```text
GET Locations
GET/POST Services
PUT LocationService
GET/POST Professionals
PUT ProfessionalLocations
PUT AvailabilityRules(Location,Professional)
POST /booking/quote
POST /availability/slots/search
POST /appointments
GET /appointments/{id}
POST /appointments/{id}/reschedule
POST /appointments/{id}/cancel
```

Segunda fatia: Payment + PaymentAttempts + webhook + Refunds.
Terceira: WhatsApp/AI/Analytics/Billing/Usage.

ScheduleBlocks foi antecipado e implementado incrementalmente em 14/10/2026,
incluindo criação idempotente, consulta, desativação e integração com slots.
A avaliação de impacto sobre Appointments permanece pendente.

## 19. Freeze v1
API Contracts v1 congelado em 2026-09-17 após aprovação dos contratos críticos de Appointment, Payment/PaymentAttempt e Refund e revisão de consistência com o ERD v1 congelado. Não foi identificada necessidade de alteração estrutural do ERD.

Novas ideias não críticas entram no backlog. Mudanças incompatíveis posteriores exigem decisão explícita e evolução/versionamento do contrato; não reabrir informalmente a baseline v1.
