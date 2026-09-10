# C4 — System Context

## 1. Objetivo

Este documento descreve o **C4 System Context (Nível 1)** da Virtual Employee Platform.

O objetivo deste nível é mostrar o sistema como uma única unidade, as pessoas que interagem com ele e os sistemas externos dos quais depende. Detalhes internos como módulos, API, banco de dados, workers e infraestrutura pertencem aos níveis seguintes do C4.

> O diagrama visual será mantido no workspace oficial do Eraser. Enquanto a geração visual estiver indisponível, este documento é a especificação textual oficial do C4 System Context.

## 2. Sistema principal

### Virtual Employee Platform

**Tipo:** Software System

Plataforma SaaS Multi-Tenant que funciona como um **funcionário virtual** para pequenos negócios e prestadores de serviços.

Responsabilidades de alto nível:

- atender clientes pelo WhatsApp;
- responder informações sobre negócio e serviços;
- consultar preços e disponibilidade;
- criar agendamentos;
- reagendar e cancelar;
- conduzir pagamentos de serviços através de checkout seguro;
- acompanhar confirmação de pagamentos;
- processar regras de cancelamento e estorno;
- enviar confirmações e lembretes;
- auxiliar o estabelecimento em situações de indisponibilidade;
- fornecer operação administrativa pelo WhatsApp;
- disponibilizar gestão através de PWA/Web;
- apresentar indicadores financeiros e operacionais;
- controlar assinatura e acesso do estabelecimento à plataforma.

## 3. Pessoas

### 3.1 Cliente Final

**Tipo:** Person

Pessoa que contrata os serviços oferecidos pelo estabelecimento.

Interage principalmente através do WhatsApp para:

- consultar serviços;
- consultar preços;
- consultar horários disponíveis;
- selecionar profissional quando aplicável;
- agendar;
- reagendar;
- cancelar;
- acessar checkout seguro;
- receber confirmação;
- receber lembretes;
- responder a propostas de reagendamento em caso de indisponibilidade do estabelecimento.

O cliente final não precisa instalar aplicativo nem criar conta para utilizar o fluxo principal.

### 3.2 Assinante / Estabelecimento

**Tipo:** Person

Pequeno negócio ou profissional prestador de serviços que contrata a Virtual Employee Platform.

Utiliza a plataforma para:

- realizar onboarding;
- configurar o estabelecimento;
- cadastrar serviços e preços;
- cadastrar profissionais;
- definir horários e regras;
- acompanhar e administrar agenda;
- consultar clientes;
- bloquear períodos;
- tratar indisponibilidades;
- consultar indicadores financeiros;
- administrar sua assinatura SaaS.

Os principais canais do assinante são:

- PWA/Web mobile-first;
- WhatsApp para comandos administrativos selecionados.

## 4. Sistemas externos

### 4.1 Meta / WhatsApp Business Platform

**Tipo:** External Software System

Canal oficial de comunicação utilizado entre clientes, estabelecimentos e a Virtual Employee Platform.

Responsabilidades externas relevantes:

- entrega e recebimento de mensagens;
- mensagens interativas;
- templates;
- eventos e webhooks;
- status de entrega quando disponível.

A Virtual Employee Platform abstrai essa integração através da capacidade de Messaging.

### 4.2 Provedor de IA / LLM

**Tipo:** External Software System

Exemplo inicial: OpenAI ou provedor equivalente.

Utilizado para interpretação de linguagem natural e experiência conversacional.

O provedor de IA não é autoridade de negócio.

Regra arquitetural:

> **IA interpreta → Backend valida → Domínio executa.**

A IA não acessa diretamente o banco de dados, não define preços, não inventa disponibilidade, não confirma pagamentos e não executa estornos.

### 4.3 Gateway de Pagamento de Serviços

**Tipo:** External Software System

Exemplo candidato: Mercado Pago ou provedor equivalente.

Responsável por:

- checkout seguro;
- Pix;
- cartão de crédito;
- cartão de débito quando suportado;
- processamento da transação;
- status de pagamento;
- webhooks;
- processamento de estornos.

O gateway processa pagamentos realizados pelo cliente final em favor do estabelecimento.

A Virtual Employee Platform não deve receber dados brutos de cartão e não considera redirect de frontend como confirmação financeira.

### 4.4 Conta/Saldo do Estabelecimento no Provedor

**Tipo:** External Financial Destination

Representa a conta, carteira ou saldo do estabelecimento no provedor de pagamento, conforme o modelo do gateway escolhido.

É o destino econômico dos valores pagos pelos clientes pelos serviços.

Regra:

> A Virtual Employee Platform não custodia os valores dos serviços prestados pelo estabelecimento.

### 4.5 SaaS Billing Provider

**Tipo:** External Software System

Responsável pelo processamento da assinatura paga pelo estabelecimento à Virtual Employee Platform.

Modelo comercial inicial:

- mensal: cartão recorrente ou Pix;
- anual com compromisso de 12 meses: cartão recorrente;
- sem boleto.

Responsabilidades externas:

- cobrança;
- recorrência;
- status da assinatura;
- retries quando aplicável;
- webhooks financeiros.

Esse fluxo é independente do pagamento de serviços.

### 4.6 Conta Bancária PJ da Plataforma

**Tipo:** External Financial Destination

Conta empresarial da Virtual Employee Platform que recebe a receita proveniente das assinaturas SaaS após processamento pelo provedor de Billing.

Não recebe automaticamente os valores pagos pelos clientes aos estabelecimentos pelos serviços.

## 5. Relacionamentos do System Context

| Origem | Destino | Relacionamento |
|---|---|---|
| Cliente Final | Meta / WhatsApp Business Platform | Conversa pelo WhatsApp |
| Meta / WhatsApp Business Platform | Virtual Employee Platform | Entrega mensagens, eventos e webhooks |
| Virtual Employee Platform | Meta / WhatsApp Business Platform | Envia respostas, confirmações, lembretes e mensagens interativas |
| Assinante / Estabelecimento | Virtual Employee Platform | Configura e administra o negócio via PWA/Web |
| Assinante / Estabelecimento | Meta / WhatsApp Business Platform | Envia comandos administrativos via WhatsApp |
| Virtual Employee Platform | Provedor de IA / LLM | Solicita interpretação de linguagem natural |
| Provedor de IA / LLM | Virtual Employee Platform | Retorna intenção/dados interpretados; não executa domínio |
| Virtual Employee Platform | Gateway de Pagamento de Serviços | Cria checkout e solicita operações financeiras autorizadas |
| Gateway de Pagamento de Serviços | Virtual Employee Platform | Envia status e webhooks financeiros |
| Gateway de Pagamento de Serviços | Conta/Saldo do Estabelecimento no Provedor | Liquida/mantém os valores dos serviços conforme regras do provedor |
| Assinante / Estabelecimento | SaaS Billing Provider | Paga a assinatura da plataforma |
| Virtual Employee Platform | SaaS Billing Provider | Gerencia ciclo/status de assinatura através de integração |
| SaaS Billing Provider | Virtual Employee Platform | Envia status e webhooks de Billing |
| SaaS Billing Provider | Conta Bancária PJ da Plataforma | Liquida a receita da assinatura SaaS conforme regras do provedor |

## 6. Fluxos financeiros separados

O contexto possui dois fluxos financeiros independentes.

### Customer Payments

```text
Cliente Final
      ↓
Gateway de Pagamento
      ↓
Conta/Saldo do Estabelecimento no Provedor
```

A plataforma orquestra a operação, mas não cobra taxa própria por appointment e não custodia os valores dos serviços.

### SaaS Billing

```text
Assinante / Estabelecimento
      ↓
SaaS Billing Provider
      ↓
Conta Bancária PJ da Plataforma
```

A assinatura SaaS não é descontada das vendas do estabelecimento.

## 7. Fluxo conceitual de decisão

Para operações comuns:

```text
Usuário
   ↓
Canal
   ↓
Virtual Employee Platform
   ↓
IA interpreta intenção
   ↓
Backend valida
   ↓
Domínio executa
```

Para operações financeiras:

```text
IA interpreta
   ↓
Usuário autoriza quando necessário
   ↓
Backend valida
   ↓
Domínio executa
   ↓
Gateway processa
   ↓
Webhook confirma
```

## 8. Boundary do sistema

### Dentro da Virtual Employee Platform

Em alto nível, pertencem ao nosso sistema:

- experiência conversacional;
- regras de negócio;
- agenda;
- catálogo de serviços;
- gestão de profissionais e clientes;
- orquestração de pagamentos;
- regras de refund;
- analytics;
- assinatura SaaS;
- metering;
- auditoria;
- PWA/Web do assinante.

### Fora da Virtual Employee Platform

São dependências externas:

- infraestrutura/canal WhatsApp da Meta;
- modelos/provedores de IA;
- gateway financeiro dos serviços;
- provedor de cobrança SaaS;
- contas/saldos financeiros dos estabelecimentos;
- conta bancária PJ da plataforma.

## 9. Restrições arquiteturais visíveis neste nível

- WhatsApp-first para o cliente final;
- PWA mobile-first para o assinante;
- Multi-Tenant;
- IA sem autoridade de negócio;
- checkout seguro externo;
- confirmação financeira por webhook/API confiável;
- separação entre Customer Payments e SaaS Billing;
- plataforma sem custódia dos valores dos serviços;
- sem taxa própria por agendamento.

## 10. Relação com os demais diagramas

A documentação C4 deve ser lida nesta ordem:

```text
C4 System Context (este documento)
        ↓
C4 Container
        ↓
Module Architecture
        ↓
Domain / Component Diagrams
```

O **System Context** explica quem usa o sistema e de quais sistemas externos ele depende.

O **C4 Container** detalha as grandes unidades executáveis e de armazenamento da Virtual Employee Platform, como PWA, Backend API, Workers e PostgreSQL.

Os diagramas **Macro Architecture** e **High-Level Architecture** existentes no Eraser continuam válidos como visões complementares e não são substituídos pelo C4.

## 11. Workspace visual oficial

Os diagramas oficiais da arquitetura são mantidos no mesmo workspace do Eraser:

https://app.eraser.io/workspace/UcBRZVFG8H5U6A3qDZwZ

Diagramas já existentes incluem:

- Macro Architecture;
- High-Level Architecture;
- C4 Container.

O C4 System Context visual deverá ser adicionado ao mesmo workspace quando a geração de diagramas estiver novamente disponível.
