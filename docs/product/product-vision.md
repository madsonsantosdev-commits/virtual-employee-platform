# Visão do Produto

## 1. Propósito

O Virtual Employee Platform é uma plataforma SaaS Multi-Tenant criada para funcionar como um **funcionário virtual** de pequenos negócios e profissionais de serviços.

O objetivo é reduzir o trabalho operacional do estabelecimento usando o WhatsApp como principal canal de relacionamento com o cliente final e inteligência artificial para interpretar conversas e intenções.

## 2. Problema

Pequenos estabelecimentos gastam tempo diariamente respondendo perguntas repetitivas, informando preços, consultando horários, criando e alterando agendamentos, cobrando clientes, confirmando presença e reorganizando a agenda quando ocorre algum imprevisto.

Grande parte desse trabalho pode ser automatizada sem obrigar o cliente final a instalar um aplicativo ou aprender uma nova ferramenta.

## 3. Proposta de valor

> Um funcionário virtual disponível pelo WhatsApp que conhece o negócio, atende clientes, organiza a agenda e auxilia o proprietário na operação.

O produto deve parecer um funcionário que trabalha para o estabelecimento, e não um software complexo que o proprietário precisa aprender a operar.

## 4. Público-alvo

Inicialmente:

- barbearias;
- salões de beleza;
- manicures e nail designers;
- profissionais de estética;
- massagistas;
- personal trainers;
- outros pequenos prestadores de serviços baseados em agenda.

## 5. Canais

### Cliente final

O cliente utiliza **WhatsApp**. Não precisa instalar aplicativo, criar conta ou acessar um portal para realizar o atendimento principal.

### Assinante

O estabelecimento utiliza:

- PWA/site responsivo mobile-first;
- comandos administrativos pelo WhatsApp.

Não haverá aplicativo Android/iOS nativo no MVP.

## 6. Capacidades do MVP

O funcionário virtual deverá ser capaz de:

- responder dúvidas sobre o estabelecimento;
- consultar serviços, preços e duração;
- consultar disponibilidade real;
- criar agendamentos;
- reagendar e cancelar;
- permitir seleção de profissional quando aplicável;
- conduzir o cliente ao checkout seguro;
- acompanhar confirmação do pagamento;
- enviar confirmação e lembrete de agendamento;
- lidar com indisponibilidades do estabelecimento;
- sugerir alternativas de reagendamento;
- processar cancelamentos conforme as regras configuradas;
- auxiliar o proprietário com consultas administrativas e financeiras.

## 7. Pagamentos de serviços

No MVP, o pagamento do serviço será sempre integral e baseado no preço registrado no catálogo.

Métodos previstos:

- Pix;
- cartão de crédito;
- cartão de débito, quando suportado pelo provedor escolhido.

Não haverá sinal, pagamento parcial ou valor negociado pela IA.

O dinheiro referente ao serviço pertence ao estabelecimento. A plataforma não deve custodiar esses valores. O gateway de pagamento processa a transação e o backend confirma o resultado através de webhook validado.

## 8. Assinatura SaaS

A cobrança da assinatura da plataforma é financeiramente independente dos pagamentos dos clientes dos estabelecimentos.

Modelo definido:

- plano mensal: cartão recorrente ou Pix;
- plano anual com compromisso de 12 meses: cartão recorrente;
- sem boleto;
- sem taxa própria da plataforma por agendamento.

## 9. Inteligência Artificial

A IA é uma camada de interpretação e experiência conversacional.

Regra central:

> **IA interpreta → Backend valida → Domínio executa → Gateway processa → Webhook confirma.**

A IA não deve acessar diretamente o banco de dados, inventar disponibilidade, definir preços, confirmar pagamentos ou executar estornos diretamente.

## 10. Diferenciais

- WhatsApp-first;
- onboarding simples;
- experiência de funcionário virtual;
- operação conversacional para cliente e proprietário;
- realocação assistida quando o estabelecimento fica indisponível;
- pagamento integrado ao agendamento;
- automação de confirmações e lembretes;
- visão financeira simples e útil;
- preço previsível, sem taxa própria por agendamento;
- foco em simplicidade em vez de ERP completo.

## 11. Fora do MVP

Não fazem parte do MVP:

- estoque;
- folha de pagamento;
- ERP completo;
- marketplace;
- aplicativo mobile nativo;
- contabilidade;
- fidelidade complexa;
- marketing avançado;
- sinal ou pagamento parcial;
- estorno parcial;
- Kubernetes;
- arquitetura prematura de microsserviços.

## 12. Métrica de sucesso do produto

O MVP será bem-sucedido quando um pequeno estabelecimento conseguir configurar o funcionário virtual, receber clientes pelo WhatsApp e realizar o ciclo completo de atendimento — descoberta do serviço, agendamento, pagamento, confirmação, alteração/cancelamento e acompanhamento — com baixa necessidade de intervenção manual do proprietário ou da plataforma.
